using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.WinUI.Views.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptMvvmBoundary()
    {
        await CheckReceiptViewportBindings();
        await CheckReceiptSavedRows();
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var masters = new PaymentServer();
        var reads = 0;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People()));
        fixture.PaymentsHandler = masters.HandleAsync;
        fixture.ReceiptsHandler = (_, _) =>
        {
            reads++;
            return Task.FromResult(PaymentServer.Json(new ReceiptListInfo(Enumerable.Range(1, 2).Select(i => new ReceiptSummaryInfo(
                Guid.Parse(i.ToString("x32")), new(2026, 11, 10), "店舗" + i, null, masters.ActiveId, i, null)).ToArray(), null)));
        };
        var root = await OpenSyntheticWindow(fixture);
        await StartSyntheticAsync();
        var view = Find<ReceiptListView>(root, "ReceiptsView");
        var model = view.ViewModel ?? throw new InvalidOperationException("一覧VMがありません。");
        var list = Find<ListView>(root, "ReceiptList");
        var text = Find<TextBox>(root, "ReceiptShop");
        Check(ReferenceEquals(view.FindName("ReceiptList"), list) && root.FindName("ReceiptList") is null &&
            ReferenceEquals(list.ItemsSource, model.Rows) && !view.IsTabStop &&
            AutomationProperties.GetAutomationId(list) == "ReceiptList" && AutomationProperties.GetAutomationId(text) == "ReceiptShop",
            "レシート一覧はViewのNameScopeとVMの唯一の行コレクションを使い、余分なTab停止を追加しない");
        Check((int)AutomationProperties.GetHeadingLevel(All(view).OfType<TextBlock>().Single(t => t.Text == "レシート")) == 2 &&
            AutomationProperties.GetLiveSetting(Find<TextBlock>(root, "ReceiptEmpty")).ToString() == "Polite",
            "レシート一覧の見出しレベルと空状態の読み上げ指定を振替一覧にそろえる");
        await FocusTransferControl(text);
        text.Text = " 未確定店舗 ";
        await Wait(() => model.SearchText == text.Text);
        await PressReceiptKey(0x0D);
        await Wait(() => window!.ReceiptHomeReady && reads == 2);
        Check(model.SearchText == " 未確定店舗 " && ReceiptEditing is null, "ViewのEnter検索は未確定の原文を共通検索へ渡し、編集を開かない");
        var keyboard = typeof(ReceiptListView).GetField("keyboard", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(view) as FormKeyboard
            ?? throw new InvalidOperationException("一覧のIME制御がありません。");
        var composing = typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(keyboard) as HashSet<TextBox>
            ?? throw new InvalidOperationException("IME観測がありません。");
        composing.Add(text);
        await PressReceiptKey(0x0D);
        await Settle();
        Check(reads == 2 && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), text), "一覧のIME合成中はEnterで検索せず、外側の共通Enter移動にも流さない");
        composing.Remove(text);
        await PressReceiptKey(0x09);
        await Wait(() => TransferControlHasFocus(root, Find<Button>(root, "ReceiptSearchButton")));
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
        Check(ReferenceEquals(model.SelectedItem, list.SelectedItem) && reads == 2 && ReceiptEditing is null,
            "レシート一覧の実上下キーはVMの同じ行を選択し、Enterや選択だけで編集/詳細GETを行わない");
    }
}
