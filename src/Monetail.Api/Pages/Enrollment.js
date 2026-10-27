"use strict";

const form = document.getElementById("enrollment-form");
const code = document.getElementById("code");
const displayName = document.getElementById("display-name");
const button = document.getElementById("register");
const message = document.getElementById("message");

// WebAuthnのJSON表現を、古いブラウザーでも扱えるバイト列へ変換する。
function decode(value) {
    return Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), char => char.charCodeAt(0));
}

// 端末の応答を、POST本文で送れるbase64urlへ変換する。
function encode(buffer) {
    return btoa(Array.from(new Uint8Array(buffer), byte => String.fromCharCode(byte)).join(""))
        .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// 同じオリジンへCSRFトークン付きで送り、失敗時は画面内で案内する。
async function post(path, body, token) {
    const response = await fetch(path, {
        method: "POST", credentials: "same-origin", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify(body)
    });
    if (!response.ok) {
        if (response.status === 429) throw new Error("操作が続いています。1分ほど待ってからやり直してください。");
        throw new Error("登録を完了できませんでした。コードと初回登録時の表示名（100文字以内）を確認してください。期限切れの場合は再発行を依頼してください。");
    }
    return response.json();
}

// 登録コードの対象にパスキーを作り、成功後は入力コードを画面から消す。
form.addEventListener("submit", async event => {
    event.preventDefault();
    button.disabled = true;
    message.textContent = "端末で本人確認を行ってください。";
    try {
        if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials)
            throw new Error("このブラウザーではパスキーを登録できません。対応するブラウザーでHTTPSの登録画面を開いてください。");
        const csrfResponse = await fetch("/auth/csrf", { credentials: "same-origin", cache: "no-store" });
        if (!csrfResponse.ok) throw new Error("登録処理を利用できません。画面を開き直してください。");
        const { token } = await csrfResponse.json();
        const options = await post("/auth/enrollment/options", { code: code.value.trim(), displayName: displayName.value }, token);
        options.challenge = decode(options.challenge);
        options.user.id = decode(options.user.id);
        for (const credential of options.excludeCredentials || []) credential.id = decode(credential.id);
        const credential = await navigator.credentials.create({ publicKey: options });
        if (!credential) throw new Error("登録を中止しました。登録操作をやり直してください。");
        const response = credential.response;
        await post("/auth/enrollment/verify", { credential: {
            id: credential.id, rawId: encode(credential.rawId), type: credential.type,
            response: {
                clientDataJSON: encode(response.clientDataJSON), attestationObject: encode(response.attestationObject),
                transports: response.getTransports ? response.getTransports() : []
            },
            clientExtensionResults: credential.getClientExtensionResults()
        } }, token);
        code.value = "";
        form.hidden = true;
        message.textContent = "パスキーを登録しました。ログイン画面へ進んでください。";
        document.getElementById("login-link").hidden = false;
    } catch (error) {
        message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
            ? "登録が中止されたか、時間切れになりました。登録操作をやり直してください。"
            : error.message;
    } finally {
        button.disabled = false;
    }
});
