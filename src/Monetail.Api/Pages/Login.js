"use strict";

const loginButton = document.getElementById("login");
const reauthenticationButton = document.getElementById("reauthenticate");
const logoutButton = document.getElementById("logout");
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
async function post(path, body) {
    const csrfResponse = await fetch("/auth/csrf", { credentials: "same-origin", cache: "no-store" });
    if (!csrfResponse.ok) throw new Error("認証処理を利用できません。時間をおいてやり直してください。");
    const { token } = await csrfResponse.json();
    const response = await fetch(path, {
        method: "POST", credentials: "same-origin", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify(body)
    });
    if (!response.ok) {
        if (response.status === 429) throw new Error("操作が続いています。1分ほど待ってからやり直してください。");
        throw new Error("操作を完了できませんでした。もう一度お試しください。");
    }
    return response.status === 204 ? null : response.json();
}

// サーバーで確認した本人と固定期限を表示し、セッション識別子をJavaScriptで保持しない。
async function refresh() {
    const response = await fetch("/auth/me", { credentials: "same-origin", cache: "no-store" });
    const loggedIn = response.ok;
    loginButton.hidden = loggedIn;
    reauthenticationButton.hidden = !loggedIn;
    logoutButton.hidden = !loggedIn;
    person.textContent = "";
    if (loggedIn) {
        const current = await response.json();
        person.textContent = `${current.displayName} としてログインしています。`;
        message.textContent = `ログイン期限: ${new Date(current.expiresAtUtc).toLocaleString()}\n本人確認日時: ${new Date(current.verifiedAtUtc).toLocaleString()}`;
    } else if (response.status !== 401) {
        throw new Error("ログイン状態を確認できません。時間をおいてやり直してください。");
    }
}

// ログインと本人再確認の共通操作として、端末の署名応答を対応する受付へ送る。
async function authenticate(path, body) {
    if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials)
        throw new Error("対応するブラウザーでHTTPSのログイン画面を開いてください。");
    const options = await post(`${path}/options`, body);
    options.challenge = decode(options.challenge);
    for (const credential of options.allowCredentials || []) credential.id = decode(credential.id);
    const credential = await navigator.credentials.get({ publicKey: options });
    if (!credential) throw new Error("本人確認を中止しました。やり直してください。");
    const response = credential.response;
    await post(`${path}/verify`, { credential: {
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
    loginButton.disabled = reauthenticationButton.disabled = logoutButton.disabled = true;
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
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
            ? "本人確認が中止されたか、時間切れになりました。やり直してください。" : error.message;
    } finally { loginButton.disabled = reauthenticationButton.disabled = logoutButton.disabled = false; }
}

loginButton.addEventListener("click", () => verifyIdentity(false));
reauthenticationButton.addEventListener("click", () => verifyIdentity(true));

// 現在のブラウザーのセッションだけを失効させ、ログイン前の表示に戻す。
logoutButton.addEventListener("click", async () => {
    reauthenticationButton.disabled = logoutButton.disabled = true;
    try {
        await post("/auth/logout", {});
        await refresh();
        message.textContent = "ログアウトしました。";
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.message;
    }
    finally { reauthenticationButton.disabled = logoutButton.disabled = false; }
});

refresh().catch(error => { message.textContent = error.message; });
