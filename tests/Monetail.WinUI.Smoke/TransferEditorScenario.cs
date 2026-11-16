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
    private static async Task FocusTransferControl(Control target)
    {
        await Wait(() => target.IsLoaded && target.ActualWidth > 0);
        if (!target.Focus(FocusState.Keyboard)) throw new InvalidOperationException("振替入力へフォーカスできません: " + target.Name);
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target));
    }
    private async Task TypeTransferName(FrameworkElement root, string text)
    {
        var box = Find<AutoSuggestBox>(root, "TransferName");
        await Wait(() => box.IsLoaded && box.ActualWidth > 0 && All(box).OfType<TextBox>().Any(t => t.IsLoaded));
        await ActivateReceiptInputWindowAsync(); var input = All(box).OfType<TextBox>().First(t => t.IsLoaded);
        input.Focus(FocusState.Keyboard); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), input));
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
        await FocusTransferControl(Find<TextBox>(root, "TransferAmount"));
    }
    private async Task DoubleClickTransfer(FrameworkElement root)
    {
        var list = Find<ListView>(root, "TransferList"); list.ScrollIntoView(list.SelectedItem);
        await Wait(() => list.ContainerFromItem(list.SelectedItem) is FrameworkElement { IsLoaded: true, ActualWidth: > 0 });
        var item = (FrameworkElement)list.ContainerFromItem(list.SelectedItem);
        var label = All(item).OfType<TextBlock>().First(t => t.ActualWidth > 0);
        var scroller = All(list).OfType<ScrollViewer>().First();
        await Wait(() => { var p = label.TransformToVisual(scroller).TransformPoint(new(label.ActualWidth / 2, label.ActualHeight / 2)); return p.X >= 0 && p.X < scroller.ViewportWidth && p.Y >= 0 && p.Y < scroller.ViewportHeight; });
        await ActivateReceiptInputWindowAsync();
        var logical = label.TransformToVisual(root).TransformPoint(new(label.ActualWidth / 2, label.ActualHeight / 2)); var scale = root.XamlRoot.RasterizationScale;
        var point = new ReceiptScreenPoint { X = (int)Math.Round(logical.X * scale), Y = (int)Math.Round(logical.Y * scale) };
        if (!ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window!), ref point) || !SetCursorPos(point.X, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var events = new uint[] { 2, 4 }.Select(flag => new ReceiptNativeInput { Type = 0, Data = new() { Mouse = new() { Flags = flag } } }).ToArray();
        if (SendInput(2, events, Marshal.SizeOf<ReceiptNativeInput>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
        // 2回の押下を別の入力ターンで配信し、同一タイムスタンプの合成クリックにしない。
        await Task.Delay(50);
        if (SendInput(2, events, Marshal.SizeOf<ReceiptNativeInput>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private async Task CheckTransferEditor()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        var active = masters.Rows.FindIndex(p => p.PaymentMethodId == masters.ActiveId); masters.Rows[active] = masters.Rows[active] with { ValidFrom = null, ValidTo = null };
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await InvokeAsync(() => Find<Button>(root, "TransferRegisterPlaceholder"));
        await Wait(() => TransferEditing is not null && Find<AutoSuggestBox>(root, "TransferName").IsLoaded);
        Check(!TransferEditing!.HasUnsavedChanges && TransferEditing.Draft.Amount is null && TransferEditing.Draft.From.PaymentMethodId is null && TransferEditing.IsMidnight == true, "振替新規は当日0時・支払方法と金額未入力で未変更");
        Check(Find<Grid>(root, "RecordContent").Visibility == Visibility.Collapsed && Find<Grid>(root, "TransferEditorPage").Visibility == Visibility.Visible && Find<Button>(root, "TransferDelete").Visibility == Visibility.Collapsed, "振替専用画面は3一覧を置き換え共通ヘッダーと左上戻るを保持");
        await FillTransfer(root, masters, " ATM "); var input = TransferEditing;
        Find<TextBox>(root, "TransferAmount").Text = "2147483648";
        await Wait(() => input!.Draft.UnresolvedAmountText == "2147483648" && !Find<Button>(root, "TransferSave").IsEnabled);
        Check(!Find<Button>(root, "TransferSave").IsEnabled && TransferEditing == input && input.Draft.UnresolvedAmountText == "2147483648", "Int32超過の生入力を保持し保存不可");
        Find<TextBox>(root, "TransferAmount").Text = "1000"; Find<TextBox>(root, "TransferMemo").Text = " ";
        await Wait(() => TransferEditing!.Draft.Amount == 1000 && TransferEditing.Draft.Memo == " " && Find<Button>(root, "TransferSave").IsEnabled);
        var keyboard = (FormKeyboard)typeof(MainWindow).GetField("transferKeyboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        await FocusTransferControl(Find<TextBox>(root, "TransferMemo")); await keyboard.HandleEnter(false, false)!;
        try { await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Find<Button>(root, "TransferSave"))); }
        catch (TaskCanceledException error) { throw new InvalidOperationException($"メモEnter後: focus={(FocusManager.GetFocusedElement(root.XamlRoot) as FrameworkElement)?.Name}; saveEnabled={Find<Button>(root, "TransferSave").IsEnabled}; composing={keyboard.IsComposing}", error); }
        Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Find<Button>(root, "TransferSave")) && server.Writes.Count == 0, "メモのEnterは保存ボタンへ移動するだけで登録しない");
        await PressReceiptKey(0x0D, 0x11); await Wait(() => TransferEditing is null && Find<ListView>(root, "TransferList").Items.Count == 1);
        var saved = server.Rows.Values.Single(); Check(saved.Amount == 1000 && saved.Memo == " " && saved.Name == " ATM " && server.Writes.Count == 1, "振替POSTは入力通知を処理してからTrimせず現在の6項目を送り201空本文とLocationから保存行へ戻る");
        Check(((TransferRow)Find<ListView>(root, "TransferList").SelectedItem).Transfer.TransferId == saved.TransferId, "登録成功後は保存行を選択する");
        Find<ListView>(root, "TransferList").SelectedIndex = 0; await Settle(); Check(server.Details == 0, "振替の単一選択は詳細GETしない");
        await DoubleClickTransfer(root); await Wait(() => TransferEditing is not null && Find<Button>(root, "TransferSave").IsLoaded);
        Check(server.Details == 1, "振替行の実ダブルクリックは詳細GETを1回だけ実行する");
        Check(!TransferEditing!.HasUnsavedChanges && TransferEditing.Revision == saved.Revision && Equals(Find<Button>(root, "TransferSave").Content, "更新"), "既存編集はID・版・保存値と更新操作を復元");
        Check(Find<Button>(root, "TransferSave").IsEnabled, "業務値が未変更でも更新の確認・無変更PUTを利用可能");
        Find<TextBox>(root, "TransferAmount").Text = "2000"; Find<DateInputBox>(root, "TransferDate").Text = DateTime.Today.AddDays(1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture); Find<TimeInputBox>(root, "TransferTime").Text = "00:00:00";
        await Wait(() => TransferEditing is { HasSavedDateChanged: true, IsMidnight: true } && Find<Button>(root, "TransferSave").IsEnabled);
        await InvokeAsync(() => Find<Button>(root, "TransferSave"));
        await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded);
        var confirmation = Find<ContentDialog>(root, "TransferConfirmationDialog").Content.ToString()!;
        Check(confirmation.Contains("00:00:00") && confirmation.Contains("日付"), "更新・日付変更・0時の警告は同じ確認ダイアログへ統合");
        await ConfirmTransfer(root, ContentDialogResult.None);
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
