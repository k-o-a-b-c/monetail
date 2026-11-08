"use strict";
const test = require("node:test"), assert = require("node:assert/strict"), vm = require("node:vm"), fs = require("node:fs"), path = require("node:path");
const pages = path.resolve(__dirname, "../../src/Monetail.Api/Pages");
function browser({ onlyOne = false, failRename = 0, cancelVerification = false } = {}) {
    const all = [], elements = new Map(), calls = [];
    let gets = 0;
    const keys = [{ id: "first-key", name: "Windows Hello", createdAtUtc: "2026-01-01T00:00:00Z" },
        { id: "second-key", name: "パスキー", createdAtUtc: "2026-01-02T00:00:00Z" }].slice(0, onlyOne ? 1 : 2);
    const buttons = new Set(["login", "reauthenticate", "logout", "add-passkey", "passkey-name-verify", "passkey-name-cancel"]);
    function node(tag = "", id = "") {
        const n = { tag, id, hidden: false, disabled: false, value: "", textContent: "", dataset: {}, listeners: {}, children: [], open: false,
            focus() { this.focused = true; }, addEventListener(event, action) { this.listeners[event] = action; },
            replaceChildren() { this.children = []; }, append(...items) { this.children.push(...items); },
            showModal() { this.open = true; }, close() { this.open = false; this.listeners.close?.(); } };
        all.push(n); return n;
    }
    function element(id) { if (!elements.has(id)) elements.set(id, node(buttons.has(id) ? "button" : "", id)); return elements.get(id); }
    const response = (status, value) => ({ status, ok: status >= 200 && status < 300, json: async () => value });
    const bytes = new Uint8Array([1, 2, 3]).buffer;
    const window = { isSecureContext: true, PublicKeyCredential() {}, confirm: () => true };
    const context = vm.createContext({ window, document: { getElementById: element, createElement: node,
        querySelectorAll: () => all.filter(n => n.tag === "button") },
        location: { origin: "https://monetail.example.test", search: "", assign() {} }, URL, URLSearchParams, Uint8Array, atob, btoa, Intl,
        navigator: { credentials: { async get() {
            gets++; if (cancelVerification) throw { name: "NotAllowedError" };
            return { id: "AQID", rawId: bytes, type: "public-key", response: { clientDataJSON: bytes, authenticatorData: bytes, signature: bytes, userHandle: bytes }, getClientExtensionResults: () => ({}) };
        } } },
        async fetch(url, options) {
            calls.push({ url, ...options, body: options?.body && JSON.parse(options.body) });
            if (url === "/auth/me") return response(200, { displayName: "本人", isGlobalAdmin: false, expiresAtUtc: "2030-01-01", verifiedAtUtc: "2026-01-01" });
            if (url === "/auth/passkeys") return response(200, keys.map(k => ({ ...k })));
            if (url === "/auth/csrf") return response(200, { token: "synthetic-token" });
            if (url === "/auth/reauthentication/options") return response(200, { challenge: "AQID", allowCredentials: [] });
            if (url === "/auth/reauthentication/verify") return response(200, {});
            if (options?.method === "PATCH") {
                assert.equal(options.headers["X-CSRF-TOKEN"], "synthetic-token");
                assert.equal(options.credentials, "same-origin");
                if (failRename) { const status = failRename; failRename = 0; return response(status, {}); }
                keys.find(k => url.endsWith(k.id)).name = JSON.parse(options.body).name;
                return response(204, null);
            }
            throw new Error("Unexpected request: " + url);
        } });
    for (const file of ["PasskeyClient.js", "Login.js"]) vm.runInContext(fs.readFileSync(path.join(pages, file), "utf8"), context, { filename: file });
    return { element, keys, calls, gets: () => gets,
        rename: () => element("passkey-list").children[0].children.find(n => n.textContent === "名前変更").listeners.click(),
        save: () => element("passkey-name-form").listeners.submit({ preventDefault() {} }),
        settled: () => new Promise(setImmediate) };
}

test("最後のパスキーも名前変更可能で、現在名を初期表示しTrim後PATCHする", async () => {
    const page = browser({ onlyOne: true }); await page.settled();
    const row = page.element("passkey-list").children[0];
    assert.equal(row.children.find(n => n.textContent === "名前変更").disabled, false);
    assert.equal(row.children.find(n => n.textContent.startsWith("削除して")).disabled, true);
    page.rename(); assert.equal(page.element("passkey-name").value, "Windows Hello");
    page.element("passkey-name").value = "  自宅のキー  "; await page.save();
    assert.equal(page.keys[0].name, "自宅のキー");
    const call = page.calls.find(c => c.method === "PATCH"); assert.deepEqual(call.body, { name: "自宅のキー" });
    assert.equal(call.url, "/auth/passkeys/first-key"); assert.equal(page.element("passkey-name-dialog").open, false);
    assert.equal(page.gets(), 0);
});
for (const value of ["  ", "あ".repeat(101), "😀".repeat(51)]) test(`名前不正は送信せず入力保持:${value.length}`, async () => {
    const page = browser(); await page.settled(); page.rename(); page.element("passkey-name").value = value;
    await page.save(); assert.ok(page.element("passkey-name-message").textContent.includes("100文字"));
    assert.equal(page.calls.filter(c => c.method === "PATCH").length, 0); assert.equal(page.element("passkey-name").value, value);
});
test("本人確認期限切れは名前を保持し、同じダイアログで再確認後に明示保存できる", async () => {
    const page = browser({ failRename: 403 }); await page.settled(); page.rename(); page.element("passkey-name").value = "入力を保持";
    await page.save(); assert.ok(page.element("passkey-name-message").textContent.includes("本人確認"));
    await page.element("passkey-name-verify").listeners.click();
    assert.equal(page.element("passkey-name").value, "入力を保持"); assert.equal(page.gets(), 1);
    assert.equal(page.calls.filter(c => c.method === "PATCH").length, 1);
    await page.save(); assert.equal(page.keys[0].name, "入力を保持");
});
for (const status of [400, 401, 404, 503]) test(`名前保存のHTTP失敗は入力保持:${status}`, async () => {
    const page = browser({ failRename: status }); await page.settled(); page.rename(); page.element("passkey-name").value = "未保存";
    await page.save(); assert.equal(page.element("passkey-name").value, "未保存"); assert.equal(page.element("passkey-name-dialog").open, true);
    assert.equal(page.calls.filter(c => c.method === "PATCH").length, 1); assert.equal(page.keys[0].name, "Windows Hello");
});
test("キャンセルと本人確認中止は保存しない", async () => {
    const page = browser({ cancelVerification: true }); await page.settled(); page.rename(); page.element("passkey-name").value = "残す";
    await page.element("passkey-name-verify").listeners.click(); assert.equal(page.element("passkey-name").value, "残す");
    await page.element("passkey-name-cancel").listeners.click(); assert.equal(page.element("passkey-name-dialog").open, false);
    assert.equal(page.calls.filter(c => c.method === "PATCH").length, 0);
});
