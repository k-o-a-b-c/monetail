using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Receipts;
using Monetail.WinUI.Views.Transfers;
using Monetail.WinUI.Views.Workspace;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostEditorWindowMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    private void RequestEditorWindowClose()
    {
        if (window is null || !PostEditorWindowMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x0010, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("Window終了要求を送信できません。");
    }
    private async Task CheckEditorPageLifetime()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer();
        var masters = new PaymentServer();
        var transfers = new TransferInputServer(masters);
        fixture.CategoriesHandler = categories.HandleAsync;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.TransfersHandler = transfers.HandleAsync;
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var host = Find<Frame>(root, "WorkspaceHost");
        var home = host.Content as RecordHomePage ?? throw new InvalidOperationException("ホームPageがありません。");
        var homeModel = home.ViewModel;
        Check(root.FindName("ReceiptEntry") is null && root.FindName("TransferEntry") is null &&
            !All(root).OfType<ReceiptEditorPage>().Any() && !All(root).OfType<TransferEditorPage>().Any(),
            "未訪問の入力PageとWindow所有の入力コントロールを生成しない");
        await window!.OpenReceiptEditorAsync(null);
        var receipt = ReceiptView;
        var receiptModel = receipt.ViewModel;
        await Wait(() => receipt.IsLoaded);
        Check(ReferenceEquals(host.Content, receipt) && home.ViewModel is null && !home.IsLoaded &&
            Find<Button>(root, "LedgerSelector").IsLoaded && Find<Button>(root, "ReceiptBack").Content.ToString() == "← 戻る",
            "レシートは標準Frameの専用Pageで共通ヘッダーと左上戻るを持ち、ホームのViewを解放する");
        Find<TextBox>(root, "ReceiptMemo").Text = "終了前の入力";
        RequestEditorWindowClose();
        await Wait(() => EditorConfirmation(receipt)?.IsLoaded == true);
        var dialog = EditorConfirmation(receipt);
        RequestEditorWindowClose();
        await Settle();
        Check(ReferenceEquals(dialog, EditorConfirmation(receipt)) && EditorConfirmation(receipt)?.Title?.ToString() == "未保存の内容があります",
            "Window終了確認は一経路だけで、二重終了要求でも確認を重複生成しない");
        await ConfirmReceipt(root, false);
        Check(ReferenceEquals(host.Content, receipt) && ReferenceEquals(receiptModel, receipt.ViewModel) &&
            Find<TextBox>(root, "ReceiptMemo").Text == "終了前の入力" && Equals(TransferRuntime("closed"), false),
            "終了確認の取消は同じPage/VM/実入力を保持する");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack"));
        await ConfirmReceipt(root, true);
        await Wait(() => Visible(root, "HomePage"));
        Check(receipt.ViewModel is null && receiptModel?.Context is null && host.BackStack.Count == 0 && host.ForwardStack.Count == 0 && !ReferenceEquals(home, host.Content) &&
            ReferenceEquals(homeModel, (host.Content as RecordHomePage)?.ViewModel),
            "通常離脱で入力PageのVM/購読を解放し、新しいホームPageが同じ一覧状態を使用する");
        await window.OpenReceiptEditorAsync(null);
        Check(!ReferenceEquals(receipt, ReceiptView) && ReceiptEditing?.NeedsLeaveConfirmation == false,
            "レシート再訪は新Pageを生成し、前回の入力を引き継がない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack"));
        await Wait(() => Visible(root, "HomePage"));
        await window.OpenTransferEditorAsync(null);
        var transfer = TransferView;
        var transferModel = transfer.ViewModel;
        await Wait(() => transfer.IsLoaded);
        Find<TextBox>(root, "TransferAmount").Text = "1x";
        RequestEditorWindowClose();
        await Wait(() => EditorConfirmation(transfer)?.IsLoaded == true);
        var transferDialog = EditorConfirmation(transfer);
        RequestEditorWindowClose();
        await Settle();
        Check(ReferenceEquals(transferDialog, EditorConfirmation(transfer)) && ReferenceEquals(host.Content, transfer),
            "振替も同じWindow終了確認入口で重複を防ぎ、入力Pageを保持する");
        await ConfirmTransfer(root, ContentDialogResult.None);
        Check(Find<TextBox>(root, "TransferAmount").Text == "1x" && ReferenceEquals(transferModel, transfer.ViewModel),
            "振替終了取消は未確定金額と同じVMを維持する");
        await InvokeAsync(() => Find<Button>(root, "TransferBack"));
        await ConfirmTransfer(root, ContentDialogResult.Secondary);
        await Wait(() => Visible(root, "HomePage"));
        Check(transfer.ViewModel is null && transferModel?.Context is null && transferModel?.PendingRequest is null && host.BackStack.Count == 0 && host.ForwardStack.Count == 0 && transfers.Writes.Count == 0,
            "振替の通常離脱はPage/要求参照を解放し、終了確認や破棄だけでは保存しない");
    }
}
