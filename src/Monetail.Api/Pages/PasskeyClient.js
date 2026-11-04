"use strict";
(() => {
// 戻り先は同じOriginの標準OAuth認可経路だけに限定する。
function authorizationTarget() {
    const value = new URLSearchParams(location.search).get("returnUrl");
    if (!value) return null;
    try {
        const target = new URL(value, location.origin);
        return target.origin === location.origin && (target.pathname === "/connect/authorize" || target.pathname === "/ledger-invitation" && !target.search) &&
            !target.username && !target.password && !target.hash ? target.href : null;
    } catch { return null; }
}
function continueAuthorization() {
    const target = authorizationTarget();
    if (!target) return false;
    location.assign(target);
    return true;
}

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
        if (response.status === 429) throw new Error("操作が続いています。時間をおいてやり直してください。");
        if (path.startsWith("/auth/passkeys") && response.status === 403)
            throw new Error("本人確認をやり直してから、もう一度操作してください。");
        if (path.startsWith("/auth/passkeys") && response.status === 409)
            throw new Error("最後のパスキーは削除できません。");
        const error = new Error("操作を完了できませんでした。もう一度お試しください。");
        error.status = response.status;
        try { error.code = (await response.json()).code; } catch { /* 本文がない共通拒否も扱う。 */ }
        throw error;
    }
    return response.status === 204 ? null : response.json();
}

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


window.MonetailPasskeys = Object.freeze({ decode, encode, send, authenticate, continueAuthorization, authorizationTarget });
})();
