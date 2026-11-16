using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private TransferEditorState? TransferEditing => (TransferEditorState?)typeof(MainWindow).GetField("transferEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
    private async Task TypeTransferName(FrameworkElement root, string text)
    {
        var box = Find<AutoSuggestBox>(root, "TransferName"); box.Focus(FocusState.Keyboard); await Settle(); await ActivateReceiptInputWindowAsync();
        var events = text.SelectMany(c => new[] {
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { Scan = c, Flags = 4 } } },
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { Scan = c, Flags = 6 } } }
        }).ToArray();
        if (SendInput((uint)events.Length, events, Marshal.SizeOf<ReceiptNativeInput>()) != events.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
        await Wait(() => TransferEditing?.Draft.Name == text);
    }
    private static Button TransferDialogFooter(FrameworkElement root, ContentDialogResult action)
    {
        var dialog = Find<ContentDialog>(root, "TransferConfirmationDialog");
        var text = action == ContentDialogResult.Primary ? dialog.PrimaryButtonText : action == ContentDialogResult.Secondary ? dialog.SecondaryButtonText : dialog.CloseButtonText;
        return All(dialog).OfType<Button>().Where(b => Equals(b.Content, text)).OrderByDescending(b => b.IsLoaded).First();
    }
    private async Task ConfirmTransfer(FrameworkElement root, ContentDialogResult action)
    {
        await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded && TransferDialogFooter(root, action).IsLoaded);
        await InvokeAsync(() => TransferDialogFooter(root, action));
        await Wait(() => !(bool)typeof(MainWindow).GetField("transferDialogOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
    }
    private async Task FillTransfer(FrameworkElement root, PaymentServer masters, string name = "ATM")
    {
        Find<TimeInputBox>(root, "TransferTime").Text = "12:34:56"; await TypeTransferName(root, name);
        var from = Find<ComboBox>(root, "TransferFrom"); var to = Find<ComboBox>(root, "TransferTo");
        from.SelectedItem = from.Items.Cast<ReceiptFilterChoice>().Single(c => c.Id == masters.ActiveId);
        to.SelectedItem = to.Items.Cast<ReceiptFilterChoice>().Single(c => c.Id == masters.OtherActiveId);
        Find<TextBox>(root, "TransferAmount").Text = "1000"; await Wait(() => Find<Button>(root, "TransferSave").IsEnabled);
    }
    private async Task CheckTransferEditor()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "TransferRegisterPlaceholder"));
        await Wait(() => TransferEditing is not null && Find<AutoSuggestBox>(root, "TransferName").IsLoaded);
        Check(!TransferEditing!.HasUnsavedChanges && TransferEditing.Draft.Amount is null && TransferEditing.Draft.From.PaymentMethodId is null && TransferEditing.IsMidnight == true, "振替新規は当日0時・支払方法と金額未入力で未変更");
        Check(Find<Grid>(root, "RecordContent").Visibility == Visibility.Collapsed && Find<Grid>(root, "TransferEditorPage").Visibility == Visibility.Visible && Find<Button>(root, "TransferDelete").Visibility == Visibility.Collapsed, "振替専用画面は3一覧を置き換え共通ヘッダーと左上戻るを保持");
        await FillTransfer(root, masters); var input = TransferEditing;
        Find<TextBox>(root, "TransferAmount").Text = "2147483648";
        Check(!Find<Button>(root, "TransferSave").IsEnabled && TransferEditing == input && input.Draft.UnresolvedAmountText == "2147483648", "Int32超過の生入力を保持し保存不可");
        Find<TextBox>(root, "TransferAmount").Text = "1000"; Find<TextBox>(root, "TransferMemo").Text = " ";
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await Wait(() => TransferEditing is null && Find<ListView>(root, "TransferList").Items.Count == 1);
        var saved = server.Rows.Values.Single(); Check(saved.Amount == 1000 && saved.Memo == " " && saved.Name == "ATM" && server.Writes.Count == 1, "振替POSTは現在の6項目を送り201空本文とLocationから保存行へ戻る");
        Check(((TransferRow)Find<ListView>(root, "TransferList").SelectedItem).Transfer.TransferId == saved.TransferId, "登録成功後は保存行を選択する");
        Find<ListView>(root, "TransferList").SelectedIndex = 0; await Settle(); Check(server.Details == 0, "振替の単一選択は詳細GETしない");
        await window!.OpenTransferEditorAsync(saved.TransferId); await Wait(() => TransferEditing is not null && Find<Button>(root, "TransferSave").IsLoaded);
        Check(!TransferEditing!.HasUnsavedChanges && TransferEditing.Revision == saved.Revision && Equals(Find<Button>(root, "TransferSave").Content, "更新"), "既存編集はID・版・保存値と更新操作を復元");
        Find<TextBox>(root, "TransferAmount").Text = "2000";
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.None);
        Check(server.Writes.Count == 1 && Find<TextBox>(root, "TransferAmount").Text == "2000", "更新確認の取消は入力と既存振替を保持");
        await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null);
        Check(server.Rows[saved.TransferId].Amount == 2000 && server.Writes[^1].Method == HttpMethod.Put, "確認した更新は既存IDのPUTで一覧へ戻る");
        await Wait(() => window.ReceiptHomeReady); await window.OpenTransferEditorAsync(saved.TransferId); await Wait(() => TransferEditing is not null);
        await InvokeAsync(() => Find<Button>(root, "TransferReuse")); await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null && server.Rows.Count == 2);
        Check(server.Rows[saved.TransferId].Amount == 2000 && server.Writes[^1].Method == HttpMethod.Post, "無変更の流用登録も現在値で別POSTを送り元振替は変更しない");
        await Wait(() => window.ReceiptHomeReady); await window.OpenTransferEditorAsync(saved.TransferId); await Wait(() => TransferEditing is not null);
        await InvokeAsync(() => Find<Button>(root, "TransferDelete")); await ConfirmTransfer(root, ContentDialogResult.None); Check(server.Rows.Count == 2, "振替削除の取消はAPIを呼ばない");
        await InvokeAsync(() => Find<Button>(root, "TransferDelete")); await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null && !server.Rows.ContainsKey(saved.TransferId));
        await Wait(() => window.ReceiptHomeReady); Check(Find<ListView>(root, "TransferList").SelectedItem is null && server.Writes[^1].Method == HttpMethod.Delete, "削除成功後は一覧から除外して選択を解除する");
    }
}
