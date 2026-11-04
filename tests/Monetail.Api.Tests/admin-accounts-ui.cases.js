"use strict";
const test = require("node:test"), assert = require("node:assert/strict"), vm = require("node:vm");
const fs = require("node:fs"), path = require("node:path");
const pages = path.resolve(__dirname, "../../src/Monetail.Api/Pages");
const url = "https://monetail.example.test/auth/enroll#token=" + "S".repeat(43);
const row = (authUserId, registrationStatus = "active", adminNote = null, displayName = null) =>
    ({ authUserId, registrationStatus, adminNote, displayName, isGlobalAdmin: false, invitationExpiresAtUtc: null });
const flush = () => new Promise(setImmediate);

// 製品のDOM操作・共通WebAuthn/CSRF処理を実行し、端末とHTTPだけを合成する。
async function browser(rows = [], { stale = false } = {}) {
    const elements = new Map(), all = [], calls = [], copies = [];
    let count = 0, getCount = 0, failure = 0, loseResponse = false, cancel = false, created, uuid = 0;
    let current = { isGlobalAdmin: true, verifiedAtUtc: new Date(Date.now() - (stale ? 360000 : 0)).toISOString() };
    function element(tag = "div") {
        const item = { tag, value: "", textContent: "", hidden: false, disabled: false, children: [], listeners: {},
            addEventListener(event, action) { this.listeners[event] = action; }, append(child) { this.children.push(child); },
            replaceChildren() { this.children = []; }, focus() {}, showModal() { this.open = true; },
            close() { this.open = false; this.listeners.close?.(); } };
        Object.defineProperty(item, "innerHTML", { set() { throw new Error("HTML injection forbidden"); } });
        all.push(item); return item;
    }
    const el = id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); };
    const document = { getElementById: el, createElement: element, querySelectorAll: () => all.filter(e => e.tag === "button") };
    for (const id of ["invite", "reload", "confirm", "close", "verify", "copy"]) el(id).tag = "button";
    const window = { isSecureContext: true, PublicKeyCredential: function () {} };
    for (const key of ["localStorage", "sessionStorage"]) Object.defineProperty(window, key, { get() { throw new Error("storage forbidden"); } });
    Object.defineProperty(document, "cookie", { get() { throw new Error("cookie forbidden"); }, set() { throw new Error("cookie forbidden"); } });
    const bytes = new Uint8Array([1, 2, 3]).buffer;
    const credential = { id: "AQID", rawId: bytes, type: "public-key", getClientExtensionResults: () => ({}),
        response: { clientDataJSON: bytes, authenticatorData: bytes, signature: bytes, userHandle: bytes } };
    const context = vm.createContext({ window, document, navigator: {
        credentials: { async get() { getCount++; if (cancel) throw { name: "NotAllowedError" }; return credential; } },
        clipboard: { async writeText(value) { copies.push(value); } } },
        crypto: { randomUUID() { return `11111111-1111-4111-8111-${String(++uuid).padStart(12, "0")}`; } },
        URL, URLSearchParams, Intl, Date, Uint8Array, atob, btoa,
        location: { origin: "https://monetail.example.test", search: "", assign() { throw new Error("unexpected navigation"); } },
        async fetch(endpoint, options = {}) {
            const body = options.body ? JSON.parse(options.body) : undefined;
            calls.push({ endpoint, method: options.method || "GET", body });
            const response = (status, data) => ({ status, ok: status >= 200 && status < 300, json: async () => data });
            if (endpoint === "/auth/csrf") return response(200, { token: "csrf" });
            if (endpoint === "/auth/me") return response(200, current);
            if (endpoint === "/auth/reauthentication/options") return response(200, { challenge: "AQID", allowCredentials: [] });
            if (endpoint === "/auth/reauthentication/verify") { current.verifiedAtUtc = new Date().toISOString(); return response(200, {}); }
            if (endpoint === "/admin/accounts" && !options.method) return response(200, [...rows]);
            if (failure) return response(failure, {});
            if (endpoint === "/admin/accounts") {
                if (created) return response(200, { authUserId: created, enrollmentUrl: null });
                created = "created"; rows.push(row(created, "invited", body.adminNote)); count++;
                if (loseResponse) { loseResponse = false; throw new TypeError("lost response"); }
                return response(201, { authUserId: created, enrollmentUrl: url });
            }
            if (endpoint.endsWith("/admin-note")) {
                rows.find(r => endpoint.includes("/" + r.authUserId + "/")).adminNote = body.adminNote;
                return response(204);
            }
            return response(options.method === "DELETE" ? 204 : 200, { enrollmentUrl: url });
        } });
    for (const file of ["PasskeyClient.js", "AdminAccounts.js"]) vm.runInContext(fs.readFileSync(path.join(pages, file), "utf8"), context, { filename: file });
    await flush();
    return { el, calls, copies, rows, count: () => count, getCount: () => getCount,
        open: () => el("invite").listeners.click(), submit: () => el("operation-form").listeners.submit({ preventDefault() {} }),
        retry: () => el("verify").listeners.click(), close: () => el("close").listeners.click(),
        fail(status) { failure = status; }, loseResponse() { loseResponse = true; }, cancel(value) { cancel = value; },
        action(id, label) { const tr = el("accounts").children.find(r => r.children[0].textContent === id);
            return tr.children.at(-1).children.find(b => b.textContent === label).listeners.click(); } };
}

test("管理一覧は状態・メモ優先の五十音・欠損・IDの安定順で、操作は状態に対応する", async () => {
    const page = await browser([row("z", "suspended", "あ"), row("b", "invited", null, "か"), row("c", "unregistered"),
        row("d", "active"), row("a2", "invited", "あ", "さ"), row("a1", "invited", "あ"), row("x", "active", "<img onerror=evil>"),
        row("d2", "active"), row("e", "unregistered", null, "さ")]);
    assert.deepEqual(page.el("accounts").children.map(r => r.children[0].textContent), ["a1", "a2", "b", "e", "c", "x", "d", "d2", "z"]);
    const buttons = id => page.el("accounts").children.find(r => r.children[0].textContent === id).children.at(-1).children.map(b => b.textContent);
    assert.deepEqual(buttons("b"), ["再発行", "取消", "メモ編集"]);
    assert.deepEqual(buttons("c"), ["再発行", "メモ編集"]);
    assert.deepEqual(buttons("z"), ["メモ編集"]);
});

test("新規招待は明示コピーだけでURLを渡し、ダイアログを閉じると破棄する", async () => {
    const page = await browser(); page.open(); page.el("note").value = "送付先";
    await page.submit();
    assert.equal(page.count(), 1); assert.equal(page.el("invitation-url").value, url); assert.equal(page.copies.length, 0);
    await page.el("copy").listeners.click(); assert.deepEqual(page.copies, [url]);
    page.close(); assert.equal(page.el("invitation-url").value, ""); assert.equal(page.el("issued").hidden, true);
    page.open(); assert.equal(page.el("invitation-url").value, "");
    assert.ok(page.calls.every(c => !c.endpoint.includes("token=")));
});

test("作成成功後の応答喪失は同じrequestIdで再送し、メモもURLも再上書きしない", async () => {
    const page = await browser(); page.open(); page.el("note").value = "送付先"; page.loseResponse();
    await page.submit(); assert.equal(page.el("note").value, "送付先");
    assert.equal(page.el("invitation-url").value, "");
    await page.submit();
    const writes = page.calls.filter(c => c.endpoint === "/admin/accounts" && c.method === "POST");
    assert.equal(writes.length, 2); assert.equal(writes[0].body.requestId, writes[1].body.requestId);
    assert.equal(page.count(), 1); assert.ok(page.el("dialog-message").textContent.includes("再発行"));
    assert.equal(page.el("invitation-url").value, "");
});

test("本人確認をキャンセルしてもメモを保持し、再試行まで更新しない", async () => {
    const page = await browser([], { stale: true }); page.open(); page.el("note").value = "未保存"; page.cancel(true);
    await page.submit(); assert.equal(page.count(), 0); assert.equal(page.el("note").value, "未保存");
    assert.ok(page.el("dialog-message").textContent.includes("中止"));
    page.cancel(false); await page.submit(); assert.equal(page.getCount(), 2); assert.equal(page.count(), 1);
});

for (const status of [400, 401, 403, 404, 409]) test(`更新エラー${status}は入力を保持し、自動保存・画面遷移をしない`, async () => {
    const page = await browser([row("target", "active", "元のメモ")]); page.action("target", "メモ編集");
    page.el("note").value = "未保存"; page.fail(status); await page.submit();
    assert.equal(page.el("note").value, "未保存"); assert.equal(page.rows[0].adminNote, "元のメモ");
    assert.equal(page.el("operation").open, true); assert.equal(page.el("invitation-url").value, "");
    assert.equal(page.el("verify").hidden, status !== 403);
    if (status === 403) { page.fail(0); await page.retry(); assert.equal(page.getCount(), 1); assert.equal(page.rows[0].adminNote, "未保存"); }
});

test("再発行と取消は対象確認後だけ実行し、取消でも一覧のアカウントは残す", async () => {
    const page = await browser([row("target", "invited", "招待先")]);
    page.action("target", "再発行"); assert.ok(page.el("target").textContent.includes("target"));
    assert.equal(page.calls.filter(c => c.method === "POST").length, 0); page.close();
    page.action("target", "再発行"); await page.submit(); assert.equal(page.el("invitation-url").value, url); page.close();
    page.action("target", "取消"); await page.submit(); assert.equal(page.el("invitation-url").value, "");
    assert.equal(page.rows.length, 1); assert.equal(page.calls.filter(c => c.method === "DELETE").length, 1);
});

test("100文字超のメモは画面内に表示し送信しない", async () => {
    const page = await browser(); page.open(); page.el("note").value = "あ".repeat(101); await page.submit();
    assert.equal(page.count(), 0); assert.ok(page.el("dialog-message").textContent.includes("100文字"));
});
