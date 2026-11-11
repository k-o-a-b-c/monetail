using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private ReceiptEditorState? ReceiptEditing => (ReceiptEditorState?)typeof(MainWindow).GetField("receiptEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
    private static Button ReceiptConfirmationFooter(FrameworkElement root, bool accept)
    {
        var dialog = Find<ContentDialog>(root, "ReceiptConfirmationDialog");
        return All(dialog).OfType<Button>().OrderByDescending(b => b.IsLoaded).First(b => Equals(b.Content, accept ? dialog.PrimaryButtonText : dialog.CloseButtonText));
    }
    private async Task ConfirmReceipt(FrameworkElement root, bool accept)
    {
        await Wait(() => {
            if (ReceiptEditing is null) throw new InvalidOperationException("未保存確認の前に編集モデルが破棄されました。");
            return Find<ContentDialog>(root, "ReceiptConfirmationDialog").IsLoaded && ReceiptConfirmationFooter(root, accept).IsLoaded && ReceiptConfirmationFooter(root, accept).ActualWidth > 0;
        });
        await InvokeAsync(() => ReceiptConfirmationFooter(root, accept));
        await Wait(() => !(bool)typeof(MainWindow).GetField("receiptDialogOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
    }
    private async Task CheckReceiptEditor()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
        var list = Find<ListView>(root, "ReceiptList");
        await Wait(() => Find<Button>(root, "NewReceiptButton").ActualWidth > 0);
        await InvokeAsync(() => Find<Button>(root, "NewReceiptButton"));
        await Wait(() => ReceiptEditing is not null && Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        var editor = ReceiptEditing!; var today = DateTime.Today;
        Check(editor.Header.TransactionAt == today && Find<DateInputBox>(root, "ReceiptDate").Text == today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) &&
            Find<TimeInputBox>(root, "ReceiptTime").Text == "00:00:00", "新規親情報は画面を開いたローカル日付・午前0時");
        Check(!editor.NeedsLeaveConfirmation && editor.Header.PaymentMethodId is null && editor.Header.TaxInclusionMode is null && editor.Header.TaxRoundingMode is null &&
            editor.Header.RecurrenceInterval is null && editor.Header.RecurrenceUnit is null && editor.Header.ShopName == "" && editor.Header.ShopLocation == "" && editor.Header.Memo == "",
            "新規親情報の空欄・未選択・周期なしは未保存変更にしない");
        Check(Visible(root, "HomePage") && Visible(root, "ReceiptEditorPage") && !Visible(root, "RecordContent") && Find<Button>(root, "LedgerSelector").IsLoaded &&
            !Find<Button>(root, "ReceiptRegister").IsEnabled && Find<Button>(root, "ReceiptUpdate").Visibility == Visibility.Collapsed, "共通ヘッダーを残しメイン領域全体を新規入力へ切替、保存は無効");
        Find<TextBox>(root, "ReceiptShopName").Text = "未保存店";
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, false);
        Check(ReferenceEquals(editor, ReceiptEditing) && editor.Header.ShopName == "未保存店" && Visible(root, "ReceiptEditorPage"), "離脱取消は同じ編集モデルと入力を保持");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, true);
        await Wait(() => ReceiptEditing is null && Visible(root, "RecordContent"));
        Check(list.Items.Count == 0 && Visible(root, "HomePage"), "明示破棄した後だけ元の一覧へ戻る");
    }
}
