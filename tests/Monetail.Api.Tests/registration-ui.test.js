"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const pages = path.resolve(__dirname, "../../src/Monetail.Api/Pages");
const token = "S".repeat(43); // 実際の招待情報を使わない。

// 製品スクリプトをそのまま実行し、DOM・WebAuthn・HTTPの境界だけを置き換える。
function browser({ mode = "invitation", hash = "#token=" + token, search = "", cancel = false, fail = "", invalid = false, globalAdmin = false } = {}) {
    const elements = new Map();
    function element(id) {
        if (!elements.has(id)) elements.set(id, { hidden: ["enrollment-form", "login"].includes(id), disabled: false,
            value: " 名前 ", textContent: "", listeners: {}, dataset: {}, focus() {}, replaceChildren() {}, append() {},
            addEventListener(event, action) { this.listeners[event] = action; } });
        return elements.get(id);
    }
    const calls = [];
    const transitions = [];
    const sequence = [];
    let createCount = 0, getCount = 0, didFail = false, loggedIn = false;
    const location = { origin: "https://monetail.example.test", pathname: mode === "public" ? "/auth/register" : "/auth/enroll",
        hash, search, assign(value) { transitions.push(value); } };
    const window = { isSecureContext: true, PublicKeyCredential: function () {},
        addEventListener(name, action) { this[name] = action; } };
    Object.defineProperty(window, "localStorage", { get() { throw new Error("persistent storage forbidden"); } });
    Object.defineProperty(window, "sessionStorage", { get() { throw new Error("persistent storage forbidden"); } });
    const document = { body: { dataset: { registration: mode } }, getElementById: element,
        querySelectorAll: () => [], createElement: () => element("item") };
    Object.defineProperty(document, "cookie", { get() { throw new Error("cookie access forbidden"); }, set() { throw new Error("cookie access forbidden"); } });
    const bytes = new Uint8Array([1, 2, 3]).buffer;
    const credential = { id: "AQID", rawId: bytes, type: "public-key", getClientExtensionResults: () => ({}),
        response: { clientDataJSON: bytes, attestationObject: bytes, authenticatorData: bytes, signature: bytes, userHandle: bytes, getTransports: () => ["internal"] } };
    const context = vm.createContext({ window, document, location, history: { replaceState(state, unused, url) {
        assert.equal(state, null);
        assert.ok(!url.includes(token));
        sequence.push("history");
        location.hash = "";
    } }, navigator: { credentials: {
        async create() { createCount++; if (cancel) throw { name: "NotAllowedError" }; return credential; },
        async get() { getCount++; return credential; }
    } }, URL, URLSearchParams, Uint8Array, atob, btoa, Intl,
    async fetch(url, options) {
        sequence.push("fetch");
        calls.push({ url, ...options, body: options?.body ? JSON.parse(options.body) : undefined });
        assert.ok(!url.includes(token));
        if (url === "/auth/csrf") return response(200, { token: "synthetic-csrf" });
        if (url === "/auth/me") return response(loggedIn ? 200 : 401, { isGlobalAdmin: globalAdmin, displayName: "名前", expiresAtUtc: "2030-01-01", verifiedAtUtc: "2026-01-01" });
        if (url === "/auth/passkeys") return response(200, []);
        if (invalid && url.includes("/options")) return response(400, {});
        if (!didFail && fail && url.endsWith(fail)) { didFail = true; throw new TypeError("synthetic network failure"); }
        if (url.endsWith("/options")) return response(200, { challenge: "AQID", user: { id: "AQID" }, excludeCredentials: [] });
        if (url === "/auth/login/verify") loggedIn = true;
        if (url === "/auth/logout") loggedIn = false;
        return response(200, { message: "ok" });
    } });
    function response(status, body) { return { status, ok: status === 200, json: async () => body }; }
    const run = file => vm.runInContext(fs.readFileSync(path.join(pages, file), "utf8"), context, { filename: file });
    run("Enrollment.js");
    assert.equal(location.hash, "");
    assert.equal(calls.length, 0); // 開封では送信・消費しない。
    run("PasskeyClient.js");
    window.DOMContentLoaded();
    return { elements, calls, sequence, transitions, run, window,
        submit: () => element("enrollment-form").listeners.submit({ preventDefault() {} }),
        login: () => element("login").listeners.click(),
        element, createCount: () => createCount, getCount: () => getCount };
}

test("招待はフラグメント除去後にPOST本文だけで渡し、成功後も明示操作まで認証しない", async () => {
    const page = browser();
    await page.submit();
    assert.equal(page.sequence[0], "history");
    const options = page.calls.find(c => c.url === "/auth/enrollment/options");
    assert.deepEqual(options.body, { token, displayName: "名前" });
    assert.equal(options.headers["X-CSRF-TOKEN"], "synthetic-csrf");
    assert.equal(page.element("enrollment-form").hidden, true);
    assert.equal(page.element("login").hidden, false);
    assert.equal(page.getCount(), 0);
    await page.login();
    assert.equal(page.getCount(), 1);
    assert.deepEqual(page.transitions, ["/auth/login"]);
});

test("公開登録はDEC-112を使用し、招待トークンを送らない", async () => {
    const page = browser({ mode: "public" });
    await page.submit();
    const options = page.calls.find(c => c.url.endsWith("/options"));
    assert.equal(options.url, "/auth/self-registration/options");
    assert.deepEqual(options.body, { displayName: "名前" });
});

test("キャンセルで表示名を失わず、同じリンクから再試行できる", async () => {
    const page = browser({ cancel: true });
    await page.submit();
    await page.submit();
    assert.equal(page.element("display-name").value, " 名前 ");
    assert.equal(page.element("enrollment-form").hidden, false);
    assert.equal(page.element("register").disabled, false);
    assert.equal(page.createCount(), 2);
    assert.equal(page.calls.filter(c => c.url.endsWith("/verify")).length, 0);
    assert.ok(page.calls.filter(c => c.url.endsWith("/options")).every(c => c.body.token === token));
});

for (const fail of ["/options", "/verify"]) test(`通信失敗${fail}で入力を保持して再試行できる`, async () => {
    const page = browser({ fail });
    await page.submit();
    assert.equal(page.element("display-name").value, " 名前 ");
    assert.equal(page.element("enrollment-form").hidden, false);
    if (fail === "/verify") assert.equal(page.element("login").hidden, false);
    await page.submit();
    assert.equal(page.element("enrollment-form").hidden, true);
    assert.ok(page.calls.filter(c => c.url.endsWith("/options")).every(c => c.body.token === token));
});

for (const hash of ["", "#token=invalid", "#token=" + token + "&other=value"]) test(`不正・欠落リンクは秘密を表示せず通信しない:${hash.length}`, () => {
    const page = browser({ hash });
    assert.equal(page.element("enrollment-form").hidden, true);
    assert.equal(page.calls.length, 0);
    assert.ok(!page.element("message").textContent.includes(token));
});

test("期限切れ・取消・使用済みの共通エラーを画面内に表示する", async () => {
    const page = browser({ invalid: true });
    await page.submit();
    assert.equal(page.element("enrollment-form").hidden, true);
    assert.equal(page.element("login").hidden, false);
    assert.ok(page.element("message").textContent.includes("発行者"));
});

test("入力不備は欄の近くへ表示し、APIへ送らない", async () => {
    const page = browser();
    page.element("display-name").value = " ";
    await page.submit();
    assert.equal(page.calls.length, 0);
    assert.ok(page.element("name-error").textContent.includes("100文字"));
});

test("登録後ログインは同じOriginのOAuth認可要求へだけ戻る", async () => {
    const page = browser({ search: "?returnUrl=" + encodeURIComponent("/connect/authorize?request_uri=synthetic") });
    await page.submit();
    await page.login();
    assert.deepEqual(page.transitions, ["https://monetail.example.test/connect/authorize?request_uri=synthetic"]);
});

for (const target of ["https://wrong.example.test/connect/authorize", "/auth/logout", "/connect/authorize#secret"]) test(`外部・任意経路へ転送しない:${target}`, async () => {
    const page = browser({ search: "?returnUrl=" + encodeURIComponent(target) });
    await page.submit();
    await page.login();
    assert.deepEqual(page.transitions, ["/auth/login"]);
});

test("通常のログイン画面も共通WebAuthn処理で認証できる", async () => {
    const page = browser({ mode: "public", hash: "" });
    page.run("Login.js");
    await new Promise(setImmediate);
    await page.element("login").listeners.click();
    assert.equal(page.getCount(), 1);
    assert.ok(page.calls.some(c => c.url === "/auth/login/verify"));
});

// 共通CIの同じNode実行で、管理画面の製品スクリプトも検証する。
require("./admin-accounts-ui.cases.js");

for (const globalAdmin of [false, true]) test(`ログイン後の管理入口は全体Adminだけに表示:${globalAdmin}`, async () => {
    const page = browser({ mode: "public", hash: "", globalAdmin });
    page.run("Login.js");
    await new Promise(setImmediate);
    await page.element("login").listeners.click();
    assert.equal(page.element("account-management").hidden, !globalAdmin);
    await page.element("logout").listeners.click();
    assert.equal(page.element("account-management").hidden, true);
});
