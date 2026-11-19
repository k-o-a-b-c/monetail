using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    // 表示/一覧リセット/家計簿ヘッダーの解除と、明示的な編集破棄の境界を検証する。
    private async Task CheckNavigationLifetime()
    {
        void Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"遷移処理がありません: {name}");
            method.Invoke(window, args);
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var masters = new PaymentServer(); var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
            fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var ledger = TransferRuntime("currentLedger"); var accountBefore = TransferRuntime("account");
            Call("ShowLogin", (object?)null);
            Check(Visible(root, "LoginPage") && ReferenceEquals(ledger, TransferRuntime("currentLedger")) &&
                ReferenceEquals(accountBefore, TransferRuntime("account")), "ログインの表示だけでは家計簿や認証コンテキストを破棄しない");
            Call("ShowPage", Find<Grid>(root, "HomePage"));
            await window!.OpenTransferEditorAsync(saved.TransferId);
            var editor = TransferEditing ?? throw new InvalidOperationException("振替編集モデルがありません。");
            var draft = editor.Draft; var amount = Find<TextBox>(root, "TransferAmount"); amount.Text = "12x";
            var date = Find<DateInputBox>(root, "TransferDate"); date.Text = "2026/";
            var details = server.Details;
            Call("ShowPage", Find<Grid>(root, "ManagementPage"));
            Check(Visible(root, "HomePage") && ReferenceEquals(editor, TransferEditing) && amount.Text == "12x",
                "未承認の画面移動ガードは入力を保持して遷移を拒否する");
            Call("ResetRecordHome");
            Check(Find<Monetail.WinUI.Views.Receipts.ReceiptListView>(root, "ReceiptsView").ViewModel is null && Find<Monetail.WinUI.Views.Transfers.TransferListView>(root, "TransfersView").ViewModel is null &&
                ReferenceEquals(editor, TransferEditing) && ReferenceEquals(draft, TransferEditing?.Draft) &&
                ReferenceEquals(amount, Find<TextBox>(root, "TransferAmount")) && amount.Text == "12x" && date.Text == "2026/" && editor.Revision == saved.Revision,
                "記録一覧のリセットは取得を終了しても振替のモデル・コントロール・未確定入力・旧版を破棄しない");
            Call("ClearLedger");
            Check(TransferRuntime("currentLedger") is null && ReferenceEquals(editor, TransferEditing) && amount.Text == "12x",
                "家計簿コンテキストの解除だけでは振替編集を破棄しない");
            Call("DiscardLedgerContents");
            Check(TransferEditing is null && TransferView.ViewModel?.PendingRequest is null && TransferRuntime("transferAuthentication") is null &&
                !Visible(root, "TransferEditorPage") && server.Details == details && server.Writes.Count == 0,
                "明示的な破棄でのみ編集を終了し、詳細GET・保存を実行しない");
        }
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            await window!.OpenReceiptEditorAsync(null);
            var editor = ReceiptEditing ?? throw new InvalidOperationException("レシート編集モデルがありません。");
            var input = ReceiptView.ViewModel?.ParentInput; var memo = Find<TextBox>(root, "ReceiptMemo"); memo.Text = "保持するメモ";
            Call("ResetRecordHome");
            Check(ReferenceEquals(editor, ReceiptEditing) && ReferenceEquals(input, ReceiptView.ViewModel?.ParentInput) &&
                ReferenceEquals(memo, Find<TextBox>(root, "ReceiptMemo")) && memo.Text == "保持するメモ",
                "記録一覧のリセットはレシート編集モデル・入力状態・コントロールも破棄しない");
            Call("ClearLedger");
            Check(ReferenceEquals(editor, ReceiptEditing) && memo.Text == "保持するメモ", "家計簿の解除とレシート編集破棄も独立する");
            Call("DiscardLedgerContents");
            Check(ReceiptEditing is null && ReceiptView.ViewModel?.ParentInput is null && !Visible(root, "ReceiptEditorPage"),
                "明示的な破棄でレシート編集も終了するが401復帰機能は追加しない");
        }
    }
}
