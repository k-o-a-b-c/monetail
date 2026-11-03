"use strict";
(() => {
    // DOM生成や他スクリプトを待たず、秘密を現在のURL・履歴から除去する。
    let invitationToken = null;
    {
        const fragment = new URLSearchParams(location.hash.slice(1));
        const value = fragment.get("token");
        if (fragment.size === 1 && /^[A-Za-z0-9_-]{43}$/.test(value || "")) invitationToken = value;
        if (location.hash) history.replaceState(null, "", location.pathname + location.search);
    }
    window.addEventListener("DOMContentLoaded", () => {
        const invitation = document.body.dataset.registration === "invitation";
        if (!invitation) invitationToken = null;
        const form = document.getElementById("enrollment-form");
        const name = document.getElementById("display-name");
        const nameError = document.getElementById("name-error");
        const register = document.getElementById("register");
        const login = document.getElementById("login");
        const heading = document.getElementById("heading");
        const message = document.getElementById("message");
        const { send, decode, encode, authenticate, continueAuthorization } = window.MonetailPasskeys;
        function invalidInvitation() {
            form.hidden = true;
            message.textContent = "この招待リンクでは登録できません。リンクを確認するか、発行者へ連絡してください。登録できたパスキーがある場合はログインをお試しください。";
            login.hidden = false;
        }
        if (invitation && !invitationToken) invalidInvitation();
        else form.hidden = false;
        form.addEventListener("submit", async event => {
            event.preventDefault();
            if (register.disabled) return;
            nameError.textContent = "";
            const displayName = name.value.trim();
            if (!displayName || displayName.length > 100) {
                nameError.textContent = "表示名を100文字以内で入力してください。";
                name.focus();
                return;
            }
            register.disabled = login.disabled = true;
            let verifying = false;
            let requestingOptions = false;
            message.textContent = "端末でパスキーを作成してください。";
            try {
                if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials)
                    throw new Error("対応するブラウザーでHTTPSの登録画面を開いてください。");
                const path = invitation ? "/auth/enrollment" : "/auth/self-registration";
                requestingOptions = true;
                const options = await send(path + "/options", invitation
                    ? { token: invitationToken, displayName } : { displayName });
                requestingOptions = false;
                options.challenge = decode(options.challenge);
                options.user.id = decode(options.user.id);
                for (const credential of options.excludeCredentials || []) credential.id = decode(credential.id);
                const credential = await navigator.credentials.create({ publicKey: options });
                if (!credential) throw new Error("登録を中止しました。登録操作をやり直してください。");
                const response = credential.response;
                verifying = true;
                await send(path + "/verify", { credential: {
                    id: credential.id, rawId: encode(credential.rawId), type: credential.type,
                    response: { clientDataJSON: encode(response.clientDataJSON), attestationObject: encode(response.attestationObject),
                        transports: response.getTransports ? response.getTransports() : [] },
                    clientExtensionResults: credential.getClientExtensionResults()
                } });
                invitationToken = null;
                form.hidden = true;
                heading.textContent = "登録完了";
                message.textContent = "登録が完了しました。";
                login.hidden = false;
            } catch (error) {
                if (invitation && requestingOptions && error.status === 400) invalidInvitation();
                else message.textContent = error.name === "NotAllowedError" || error.name === "AbortError"
                    ? "登録が中止されたか、時間切れになりました。登録操作をやり直してください。"
                    : "登録を完了できませんでした。入力を保持しています。時間をおいてやり直してください。";
                if (verifying) {
                    message.textContent += "\n登録できたパスキーがある場合は、ログインをお試しください。";
                    login.hidden = false;
                }
            } finally { register.disabled = login.disabled = false; }
        });
        // 明示したログイン操作で同じ画面から標準WebAuthn認証を開始する。
        login.addEventListener("click", async () => {
            if (login.disabled) return;
            login.disabled = register.disabled = true;
            try {
                let timeZone = null;
                try { timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch { /* サーバーの地域へ補完する。 */ }
                await authenticate("/auth/login", { timeZone });
                if (!continueAuthorization()) location.assign("/auth/login");
            } catch {
                message.textContent = "ログインできませんでした。パスキーを確認してやり直してください。";
            } finally { login.disabled = register.disabled = false; }
        });
    }, { once: true });
})();
