"use strict";
(() => {
const key = "monetail.ledgerInvitation", el = id => document.getElementById(id);
let state, preview, previewAccount, busy = false;
const uuid = value => typeof value === "string" && /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i.test(value);
const same = (a, b) => a?.replaceAll("-", "").toLowerCase() === b?.replaceAll("-", "").toLowerCase();
function message(text) { el("message").textContent = text; }
function buttons(...ids) { for (const id of ["preview", "accept", "check", "invitation-login"]) el(id).hidden = !ids.includes(id); }
function save() { sessionStorage.setItem(key, JSON.stringify(state)); }
function clear() { sessionStorage.removeItem(key); state = null; }
function complete() { clear(); buttons(); message("家計簿に参加しました。Windows版で家計簿を開いてください。"); }
function unknown() { buttons("check"); message("参加できたか確認できませんでした。"); }
async function me() {
    const response = await fetch("/auth/me", { credentials: "same-origin", cache: "no-store" });
    if (response.status === 401) { buttons("invitation-login"); message("ログインしてから操作を続けてください。"); return null; }
    if (!response.ok) throw new Error();
    const user = await response.json();
    if (!uuid(user.authUserId)) throw new Error();
    if (state.pending && !same(user.authUserId, state.pending.accountId)) {
        buttons(); message("元の操作と異なるアカウントです。元のアカウントでログインし直してください。");
        return null;
    }
    return user;
}
async function run(action) {
    if (busy) return; busy = true;
    for (const button of document.querySelectorAll("button")) button.disabled = true;
    try { await action(); }
    catch { if (state?.pending) unknown(); else { buttons("preview"); message("確認できませんでした。通信状態を確認して再試行してください。"); } }
    finally { busy = false; for (const button of document.querySelectorAll("button")) button.disabled = false; }
}
async function showPreview() {
    if (!state || state.pending) { if (state?.pending) unknown(); return; }
    const viewer = await me(); if (!viewer) return;
    try {
        const result = await window.MonetailPasskeys.send("/ledger-invitations/preview", { token: state.token });
        if (!uuid(result.ledgerId) || typeof result.ledgerName !== "string" || !["valid", "already_member"].includes(result.status)) throw new Error();
        previewAccount = viewer.authUserId; preview = result; el("ledger-name").textContent = result.ledgerName;
        if (result.status === "already_member") { clear(); buttons(); message("この家計簿には参加済みです。"); }
        else { buttons("accept"); message(`「${result.ledgerName}」に参加しますか？`); }
    } catch (error) {
        if (error.status === 401) { buttons("invitation-login"); message("ログインしてください。"); }
        else if ([400, 404, 409].includes(error.status)) { clear(); buttons(); message("この招待は無効・期限切れ・使用済み・取消済みのため利用できません。"); }
        else throw error;
    }
}
async function accept() {
    if (!state || (!preview && !state.pending)) return;
    const user = await me(); if (!user) return;
    if (!state.pending && !same(user.authUserId, previewAccount)) { buttons(); message("確認時と異なるアカウントです。招待を開き直してください。"); return; }
    // 保存できた場合だけ送信する。応答より先に結果不明を記録し、再読込でも自動再送しない。
    state.pending ??= { accountId: user.authUserId, ledgerId: preview.ledgerId };
    try { save(); } catch { buttons(); message("一時保存できないため参加処理を進められません。"); return; }
    unknown();
    try {
        const result = await window.MonetailPasskeys.send("/ledger-invitations/accept", { token: state.token });
        if (result.status === "joined" && same(result.ledger?.ledgerId, state.pending.ledgerId)) complete();
        else if (result.status === "already_member" && same(result.ledgerId, state.pending.ledgerId)) complete();
        else unknown();
    } catch (error) {
        if (error.status === 401) { buttons("invitation-login"); message("ログインしてから参加状況を確認してください。"); }
        else unknown();
    }
}
async function check() {
    if (!state?.pending || !await me()) return;
    const response = await fetch("/ledgers/" + encodeURIComponent(state.pending.ledgerId), { credentials: "same-origin", cache: "no-store" });
    if (response.status === 200) {
        const result = await response.json();
        if (!same(result.ledgerId, state.pending.ledgerId)) throw new Error();
        complete();
    } else if (response.status === 404) {
        buttons("check", "accept"); el("check").textContent = "参加状況を再確認";
        message("現時点では参加を確認できません。元の参加要求が失敗したとは限りません。再確認または参加を選んでください。");
    } else if (response.status === 401) { buttons("invitation-login"); message("ログインしてから参加状況を確認してください。"); }
    else unknown();
}
el("preview").addEventListener("click", () => run(showPreview));
el("accept").addEventListener("click", () => run(accept));
el("check").addEventListener("click", () => run(check));
el("back").addEventListener("click", () => { try { clear(); location.assign("/auth/login"); } catch { buttons(); message("一時保存を削除できませんでした。"); } });
// フラグメントは保存・通信に先立って除去する。結果不明の記録を別リンクで置き換えない。
const hash = location.hash;
history.replaceState(null, "", "/ledger-invitation");
try {
    const stored = sessionStorage.getItem(key);
    state = stored ? JSON.parse(stored) : null;
    if (state && (!/^[A-Za-z0-9_-]{43}$/.test(state.token) || state.pending && (!uuid(state.pending.accountId) || !uuid(state.pending.ledgerId)))) throw new Error();
    if (hash && !state?.pending) {
        if (!/^#token=[A-Za-z0-9_-]{43}$/.test(hash)) { clear(); throw new Error(); }
        state = { token: hash.slice(7) }; save();
    }
    if (!state) { buttons(); message("招待リンクを開き直してください。"); }
    else if (state.pending) unknown();
    else run(showPreview);
} catch { buttons(); message("招待情報を一時保存・復元できません。元のリンクから開き直してください。"); }
})();
