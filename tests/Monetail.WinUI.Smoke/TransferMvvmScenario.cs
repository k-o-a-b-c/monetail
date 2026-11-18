using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.WinUI.Views.Receipts;
using Monetail.WinUI.Views.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferMvvmBoundary()
    {
        await CheckTransferViewportTriggers();
        await CheckTransferRegistrationCommand();
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer();
        var reads = 0;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.TransfersHandler = (_, _) =>
        {
            reads++;
            return Task.FromResult(PaymentServer.Json(new TransferListInfo(Enumerable.Range(1, 2).Select(i => new TransferSummaryInfo(
                Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "振替" + i, masters.ActiveId, masters.OtherActiveId, i, null)).ToArray(), null)));
        };
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var view = Find<TransferListView>(root, "TransfersView");
        var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
        var list = Find<ListView>(root, "TransferList");
        var text = Find<TextBox>(root, "RecordKeyword");
        await Settle();
        var receiptView = Find<ReceiptListView>(root, "ReceiptsView");
        static double Top(FrameworkElement element, FrameworkElement scope) =>
            element.TransformToVisual(scope).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        Check(Top(text, root) < Top(receiptView, root) && Top(text, root) < Top(view, root) &&
            Top(Find<Button>(root, "NewReceiptButton"), receiptView) < Top(Find<ListView>(root, "ReceiptList"), receiptView) &&
            Top(Find<Button>(root, "TransferRegisterPlaceholder"), view) < Top(list, view),
            "共通キーワード検索は一覧の上に置き、レシートと振替の見出し・登録は各一覧の同じ段に表示する");
        Check(All(receiptView).OfType<TextBox>().Count() == 0 && All(view).OfType<TextBox>().Count() == 0 &&
            All(root).OfType<TextBox>().Count(item => item.Name == "RecordKeyword") == 1,
            "各一覧はキーワード入力を持たず、共通フィルターの1欄だけを使う");
        Check(ReferenceEquals(view.FindName("TransferList"), list) && root.FindName("TransferList") is null &&
            ReferenceEquals(list.ItemsSource, model.Rows) && !view.IsTabStop,
            "振替一覧はViewのNameScopeとVMの唯一の行コレクションを使い、余分なTab停止を追加しない");
        await FocusTransferControl(text);
        text.Text = " 未確定ＡＴＭ ";
        await PressReceiptKey(0x0D);
        await Wait(() => window!.TransferHomeReady && reads == 2);
        Check(model.SearchText == " 未確定ＡＴＭ " && TransferEditing is null, "ViewのEnter検索は未確定の原文を共通検索へ渡し、編集を開かない");
        var keyboard = typeof(MainWindow).GetField("recordKeyboard", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as FormKeyboard
            ?? throw new InvalidOperationException("一覧のIME制御がありません。");
        var composing = typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(keyboard) as HashSet<TextBox>
            ?? throw new InvalidOperationException("IME観測がありません。");
        composing.Add(text);
        await PressReceiptKey(0x0D);
        await Settle();
        Check(reads == 2 && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), text), "一覧のIME合成中はEnterで検索せず、外側の共通Enter移動にも流さない");
        composing.Remove(text);
        await PressReceiptKey(0x09);
        await Wait(() => TransferControlHasFocus(root, Find<Button>(root, "RecordKeywordSearch")));
        await PressReceiptKey(0x09, 0x10);
        await Wait(() => TransferControlHasFocus(root, text));
        Check(reads == 2, "検索欄と検索ボタンの実Tab/Shift+Tab順を維持し、移動だけでは取得しない");
        list.SelectedIndex = 0;
        list.ScrollIntoView(list.Items[0]);
        await Wait(() => list.ContainerFromIndex(0) is ListViewItem { IsLoaded: true, ActualWidth: > 0 });
        ((ListViewItem)list.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
        await PressReceiptKey(0x28);
        await Wait(() => list.SelectedIndex == 1);
        await PressReceiptKey(0x26);
        await Wait(() => list.SelectedIndex == 0);
        await PressReceiptKey(0x0D);
        await Settle();
        Check(ReferenceEquals(model.SelectedItem, list.SelectedItem) && reads == 2 && TransferEditing is null,
            "振替一覧の実上下キーはVMの同じ行を選択し、Enterや選択だけで編集/詳細GETを行わない");
    }
}
