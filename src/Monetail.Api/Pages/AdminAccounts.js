"use strict";
(() => {
const { send, authenticate } = window.MonetailPasskeys;
const el = id => document.getElementById(id);
const labels = { invited: "招待中", unregistered: "未登録・招待なし", active: "利用中", suspended: "停止中" };
const states = Object.keys(labels);
const collator = new Intl.Collator("ja");
let current, draft, busy = false;

// 状態はAPIの判定を使い、メモ優先の五十音順とIDで表示順を安定させる。
function compare(a, b) {
    const state = states.indexOf(a.registrationStatus) - states.indexOf(b.registrationStatus);
    if (state) return state;
    const left = a.adminNote || a.displayName, right = b.adminNote || b.displayName;
    if (!left && right) return 1;
    if (left && !right) return -1;
    return (left && right ? collator.compare(left, right) : 0) || (a.authUserId < b.authUserId ? -1 : a.authUserId > b.authUserId ? 1 : 0);
}
async function read(path) {
    const response = await fetch(path, { credentials: "same-origin", cache: "no-store" });
    if (!response.ok) { const error = new Error(); error.status = response.status; throw error; }
    return response.json();
}
async function identity() {
    current = await read("/auth/me");
    if (!current.isGlobalAdmin) { const error = new Error(); error.status = 403; throw error; }
}
async function load() {
    const rows = await read("/admin/accounts");
    el("accounts").replaceChildren();
    for (const account of rows.sort(compare)) {
        const row = document.createElement("tr");
        for (const value of [account.authUserId, account.displayName ?? "—", account.adminNote ?? "—",
            account.isGlobalAdmin ? "全体管理者" : "一般ユーザー", labels[account.registrationStatus],
            account.invitationExpiresAtUtc ? new Date(account.invitationExpiresAtUtc).toLocaleString() : "—"]) {
            const cell = document.createElement("td"); cell.textContent = value; row.append(cell);
        }
        const actions = document.createElement("td");
        const modes = account.registrationStatus === "invited" ? ["reissue", "cancel", "note"]
            : account.registrationStatus === "unregistered" ? ["reissue", "note"] : ["note"];
        for (const mode of modes) {
            const button = document.createElement("button"); button.type = "button";
            button.textContent = { reissue: "再発行", cancel: "取消", note: "メモ編集" }[mode];
            button.disabled = busy; button.addEventListener("click", () => open(mode, account)); actions.append(button);
        }
        row.append(actions); el("accounts").append(row);
    }
    el("message").textContent = `${rows.length}件のアカウント`;
}
function setBusy(value) {
    busy = value;
    for (const button of document.querySelectorAll("button")) button.disabled = value;
    el("note").disabled = value;
}
function open(mode, account) {
    draft = { mode, id: account?.authUserId, requestId: mode === "create" ? crypto.randomUUID() : null };
    el("dialog-title").textContent = { create: "新規招待", reissue: "招待を再発行", cancel: "招待を取り消す", note: "招待先メモを編集" }[mode];
    el("target").textContent = account ? `${account.adminNote || account.displayName || "名称未設定"}（${account.authUserId}）` : "一般ユーザーを招待します。招待の有効期間は7日間です。";
    el("note-field").hidden = mode !== "create" && mode !== "note";
    el("note").value = account?.adminNote ?? "";
    el("dialog-message").textContent = mode === "reissue" ? "以前の招待リンクは使えなくなります。再発行しますか？"
        : mode === "cancel" ? "招待リンクを取り消しますか？ アカウントは削除されません。" : "";
    el("confirm").textContent = { create: "招待を発行", reissue: "再発行", cancel: "取り消す", note: "保存" }[mode];
    el("confirm").hidden = false; el("verify").hidden = true; el("issued").hidden = true;
    el("invitation-url").value = ""; el("close").textContent = "キャンセル";
    el("operation").showModal();
    (el("note-field").hidden ? el("confirm") : el("note")).focus();
}
function errorMessage(error) {
    if (error.status === 401) return "ログインが失効しました。別タブで同じアカウントにログインし直し、再試行してください。入力は保持しています。";
    if (error.status === 403) return "権限または本人確認を確認できません。本人確認をやり直してください。入力は保持しています。";
    if (error.status === 409) return "対象の状態が変わったため操作できません。一覧を確認してください。入力は保持しています。";
    if (error.status === 404) return "対象アカウントを確認できません。入力は保持しています。";
    if (error.status === 400) return "入力を確認してください。招待先メモは100文字以内です。";
    if (error.name === "NotAllowedError" || error.name === "AbortError") return "本人確認を中止しました。入力は保持しています。もう一度お試しください。";
    return "通信または操作に失敗しました。処理済みの可能性があります。一覧を確認し、このダイアログから再試行してください。新規招待には同じ要求IDを使います。";
}
async function perform(forceVerification = false) {
    if (busy || !draft) return;
    if (el("note").value.length > 100) { el("dialog-message").textContent = "招待先メモは100文字以内で入力してください。"; return; }
    setBusy(true); el("verify").hidden = true;
    try {
        await identity();
        const verified = new Date(current.verifiedAtUtc).getTime(), now = Date.now();
        if (forceVerification || !Number.isFinite(verified) || verified > now || now - verified >= 300000) {
            el("dialog-message").textContent = "端末でパスキーを選んで本人確認してください。";
            await authenticate("/auth/reauthentication", {});
            await identity();
        }
        const base = "/admin/accounts", target = base + "/" + encodeURIComponent(draft.id);
        const result = draft.mode === "create" ? await send(base, { requestId: draft.requestId, adminNote: el("note").value })
            : draft.mode === "note" ? await send(target + "/admin-note", { adminNote: el("note").value }, "PATCH")
            : await send(target + "/enrollment-invitation", {}, draft.mode === "cancel" ? "DELETE" : "POST");
        el("confirm").hidden = true; el("close").textContent = "閉じる";
        if (result?.enrollmentUrl) {
            el("invitation-url").value = result.enrollmentUrl; el("issued").hidden = false;
            el("dialog-message").textContent = "発行しました。招待URLはこの場でのみ表示できます。送付先を限定してください。";
        } else {
            el("dialog-message").textContent = draft.mode === "create"
                ? `作成済みのアカウントです（${result.authUserId}）。URLは復元できません。一覧から招待を再発行してください。`
                : "保存しました。";
        }
        draft = null;
        await load().catch(() => { el("message").textContent = "操作は成功しましたが一覧を取得できません。再読み込みしてください。"; });
    } catch (error) {
        el("dialog-message").textContent = errorMessage(error);
        el("verify").hidden = error.status !== 403;
        await load().catch(() => {});
    } finally { setBusy(false); }
}
el("invite").addEventListener("click", () => open("create"));
el("operation-form").addEventListener("submit", event => { event.preventDefault(); return perform(); });
el("verify").addEventListener("click", () => perform(true));
el("close").addEventListener("click", () => { if (!busy) el("operation").close(); });
el("operation").addEventListener("cancel", event => { if (busy) event.preventDefault(); });
el("operation").addEventListener("close", () => { draft = null; el("invitation-url").value = ""; el("issued").hidden = true; el("note").value = ""; });
el("copy").addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(el("invitation-url").value); el("dialog-message").textContent = "コピーしました。送付先を限定してください。"; }
    catch { el("dialog-message").textContent = "コピーできませんでした。招待URLを選択してコピーしてください。"; }
});
el("reload").addEventListener("click", () => refresh());
async function refresh() {
    setBusy(true);
    try { await identity(); await load(); }
    catch (error) { el("message").textContent = errorMessage(error); }
    finally { setBusy(false); }
}
refresh();
})();
