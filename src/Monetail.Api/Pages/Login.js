"use strict";

// OAuthの認可要求へだけ戻り、外部URLへの転送を許可しない。
function continueAuthorization() {
    const value = new URLSearchParams(location.search).get("returnUrl");
    if (!value) return;
    let target;
    try { target = new URL(value, location.origin); } catch { return; }
    if (target.origin === location.origin && target.pathname === "/connect/authorize" &&
        !target.username && !target.password && !target.hash) location.assign(target.href);
}

const loginButton = document.getElementById("login");
const reauthenticationButton = document.getElementById("reauthenticate");
const logoutButton = document.getElementById("logout");
const passkeysSection = document.getElementById("passkeys");
const passkeyList = document.getElementById("passkey-list");
const addPasskeyButton = document.getElementById("add-passkey");
const person = document.getElementById("person");
const message = document.getElementById("message");

// WebAuthnのJSON表現を、ブラウザーに渡すバイト列へ変換する。
function decode(value) {
    return Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), char => char.charCodeAt(0));
}

// 署名などのバイト列を、POST本文用のbase64urlへ変換する。
function encode(buffer) {
    return btoa(Array.from(new Uint8Array(buffer), byte => String.fromCharCode(byte)).join(""))
        .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// 匿名・ログイン後の本人に対応するCSRFトークンを、その操作直前に取得する。
async function send(path, body, method = "POST") {
    const csrfResponse = await fetch("/auth/csrf", { credentials: "same-origin", cache: "no-store" });
    if (!csrfResponse.ok) throw new Error("認証処理を利用できません。時間をおいてやり直してください。");
    const { token } = await csrfResponse.json();
    const response = await fetch(path, {
        method, credentials: "same-origin", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify(body)
    });
    if (!response.ok) {
        if (response.status === 429) throw new Error("操作が続いています。1分ほど待ってからやり直してください。");
        if (path.startsWith("/auth/passkeys") && response.status === 403)
            throw new Error("本人確認をやり直してから、もう一度操作してください。");
        if (path.startsWith("/auth/passkeys") && response.status === 409)
            throw new Error("最後のパスキーは削除できません。");
        throw new Error("操作を完了できませんでした。もう一度お試しください。");
    }
    return response.status === 204 ? null : response.json();
}

// 認証済みアカウントの表示名と固定期限を表示し、セッション識別子をJavaScriptで保持しない。
async function refresh() {
    const response = await fetch("/auth/me", { credentials: "same-origin", cache: "no-store" });
    const loggedIn = response.ok;
    loginButton.hidden = loggedIn;
    reauthenticationButton.hidden = !loggedIn;
    logoutButton.hidden = !loggedIn;
    passkeysSection.hidden = !loggedIn;
    passkeyList.replaceChildren();
    person.textContent = "";
    if (loggedIn) {
        const current = await response.json();
        person.textContent = `${current.displayName} としてログインしています。`;
        message.textContent = `ログイン期限: ${new Date(current.expiresAtUtc).toLocaleString()}\n本人確認日時: ${new Date(current.verifiedAtUtc).toLocaleString()}`;
        await refreshPasskeys();
    } else if (response.status !== 401) {
        throw new Error("ログイン状態を確認できません。時間をおいてやり直してください。");
    }
}

// ログインと本人再確認の共通操作として、端末の署名応答を対応する受付へ送る。
async function authenticate(path, body) {
    if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials)
        throw new Error("対応するブラウザーでHTTPSのログイン画面を開いてください。");
    const options = await send(`${path}/options`, body);
    options.challenge = decode(options.challenge);
    for (const credential of options.allowCredentials || []) credential.id = decode(credential.id);
    const credential = await navigator.credentials.get({ publicKey: options });
    if (!credential) throw new Error("本人確認を中止しました。やり直してください。");
    const response = credential.response;
    await send(`${path}/verify`, { credential: {
        id: credential.id, rawId: encode(credential.rawId), type: credential.type,
        response: {
            clientDataJSON: encode(response.clientDataJSON), authenticatorData: encode(response.authenticatorData),
            signature: encode(response.signature), userHandle: response.userHandle ? encode(response.userHandle) : null
        },
        clientExtensionResults: credential.getClientExtensionResults()
    } });
}

// ログインまたは本人再確認の間は、重複操作を止めて終了後に現在の状態を表示する。
async function verifyIdentity(reauthentication) {
    setBusy(true);
    message.textContent = "端末でパスキーを選んで本人確認を行ってください。";
    try {
        if (reauthentication) {
            await authenticate("/auth/reauthentication", {});
        } else {
            let timeZone = null;
            try { timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch { /* サーバーの地域へ補完する。 */ }
            await authenticate("/auth/login", { timeZone });
        }
        await refresh();
        if (!reauthentication) continueAuthorization();
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
            ? "本人確認が中止されたか、時間切れになりました。やり直してください。" : error.message;
    } finally { setBusy(false); }
}

loginButton.addEventListener("click", () => verifyIdentity(false));
reauthenticationButton.addEventListener("click", () => verifyIdentity(true));

// 現在のブラウザーのセッションだけを失効させ、ログイン前の表示に戻す。
logoutButton.addEventListener("click", async () => {
    setBusy(true);
    try {
        await send("/auth/logout", {});
        await refresh();
        message.textContent = "ログアウトしました。";
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.message;
    }
    finally { setBusy(false); }
});

// 認証や鍵の変更中は、同じ画面の操作が重ならないようにする。
function setBusy(busy) {
    for (const button of document.querySelectorAll("button"))
        button.disabled = busy || button.dataset.lastPasskey === "true";
}

// 本人の鍵の表示名と登録日時を表示し、最後の鍵の削除操作を無効にする。
async function refreshPasskeys() {
    const response = await fetch("/auth/passkeys", { credentials: "same-origin", cache: "no-store" });
    if (!response.ok) throw new Error("パスキー一覧を取得できません。時間をおいてやり直してください。");
    const keys = await response.json();
    for (const key of keys) {
        const item = document.createElement("li");
        const label = document.createElement("span");
        label.textContent = `${key.name}（登録日時: ${new Date(key.createdAtUtc).toLocaleString()}）`;
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = "削除して全端末からログアウト";
        button.dataset.lastPasskey = String(keys.length <= 1);
        button.disabled = keys.length <= 1 || addPasskeyButton.disabled;
        button.addEventListener("click", () => removePasskey(key.id));
        item.append(label, button);
        passkeyList.append(item);
    }
}

// ログイン中のアカウントへパスキーを追加し、登録検証後の一覧を取り直す。
addPasskeyButton.addEventListener("click", async () => {
    setBusy(true);
    message.textContent = "端末で新しいパスキーを作成してください。";
    try {
        const options = await send("/auth/passkeys/options", {});
        options.challenge = decode(options.challenge);
        options.user.id = decode(options.user.id);
        for (const credential of options.excludeCredentials || []) credential.id = decode(credential.id);
        const credential = await navigator.credentials.create({ publicKey: options });
        if (!credential) throw new Error("追加を中止しました。やり直してください。");
        const response = credential.response;
        await send("/auth/passkeys/verify", { credential: {
            id: credential.id, rawId: encode(credential.rawId), type: credential.type,
            response: { clientDataJSON: encode(response.clientDataJSON), attestationObject: encode(response.attestationObject),
                transports: response.getTransports ? response.getTransports() : [] },
            clientExtensionResults: credential.getClientExtensionResults()
        } });
        await refresh();
        message.textContent += "\nパスキーを追加しました。";
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
            ? "追加が中止されたか、時間切れになりました。やり直してください。" : error.message;
    } finally { setBusy(false); }
});

// 本人が選んだ鍵の削除を確認し、成功後は全セッション失効を画面に反映する。
async function removePasskey(id) {
    if (!window.confirm("このパスキーを削除し、すべての端末からログアウトします。よろしいですか？")) return;
    setBusy(true);
    try {
        await send(`/auth/passkeys/${encodeURIComponent(id)}`, {}, "DELETE");
        await refresh();
        message.textContent = "パスキーを削除しました。残っているパスキーでログインしてください。";
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.message;
    } finally { setBusy(false); }
}

refresh().catch(error => { message.textContent = error.message; });
