"use strict";

const loginButton = document.getElementById("login");
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
        throw new Error("操作を完了できませんでした。パスキーでのログインをやり直してください。");
    }
    return response.status === 204 ? null : response.json();
}

// サーバーで確認した本人と固定期限を表示し、セッション識別子をJavaScriptで保持しない。
async function refresh() {
    const response = await fetch("/auth/me", { credentials: "same-origin", cache: "no-store" });
    const loggedIn = response.ok;
    loginButton.hidden = loggedIn;
    logoutButton.hidden = !loggedIn;
    person.textContent = "";
    if (loggedIn) {
        const current = await response.json();
        person.textContent = `${current.displayName} としてログインしています。`;
        message.textContent = `ログイン期限: ${new Date(current.expiresAtUtc).toLocaleString()}`;
    } else if (response.status !== 401) {
        throw new Error("ログイン状態を確認できません。時間をおいてやり直してください。");
    }
}

// 端末でパスキーを選び、Identity検証に必要な署名応答を送る。
loginButton.addEventListener("click", async () => {
    loginButton.disabled = true;
    message.textContent = "端末でパスキーを選んで本人確認を行ってください。";
    try {
        if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials)
            throw new Error("対応するブラウザーでHTTPSのログイン画面を開いてください。");
        let timeZone = null;
        try { timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch { /* サーバーの地域へ補完する。 */ }
        const options = await post("/auth/login/options", { timeZone });
        options.challenge = decode(options.challenge);
        for (const credential of options.allowCredentials || []) credential.id = decode(credential.id);
        const credential = await navigator.credentials.get({ publicKey: options });
        if (!credential) throw new Error("ログインを中止しました。やり直してください。");
        const response = credential.response;
        await post("/auth/login/verify", { credential: {
            id: credential.id, rawId: encode(credential.rawId), type: credential.type,
            response: {
                clientDataJSON: encode(response.clientDataJSON), authenticatorData: encode(response.authenticatorData),
                signature: encode(response.signature), userHandle: response.userHandle ? encode(response.userHandle) : null
            },
            clientExtensionResults: credential.getClientExtensionResults()
        } });
        await refresh();
    } catch (error) {
        message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
            ? "ログインが中止されたか、時間切れになりました。やり直してください。" : error.message;
    } finally { loginButton.disabled = false; }
});

// 現在のブラウザーのセッションだけを失効させ、ログイン前の表示に戻す。
logoutButton.addEventListener("click", async () => {
    logoutButton.disabled = true;
    try {
        await post("/auth/logout", {});
        await refresh();
        message.textContent = "ログアウトしました。";
    } catch (error) {
        await refresh().catch(() => {});
        message.textContent = error.message;
    }
    finally { logoutButton.disabled = false; }
});

refresh().catch(error => { message.textContent = error.message; });
