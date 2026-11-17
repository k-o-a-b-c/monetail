using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckLedgerChoiceControl()
    {
        using (var fixture = new SyntheticConnection(saved: true) { ChoiceCount = 2 })
        {
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var originalWindow = window;
            var page = Find<LedgerChoiceView>(root, "LedgerChoicePage");
            var choices = Find<ListView>(page, "LedgerChoices"); var open = Find<Button>(page, "OpenLedgerButton");
            var data = page.Ledgers;
            var notifications = new List<(object? Sender, Guid Id)>();
            page.OpenRequested += (sender, id) => notifications.Add((sender, id));
            Check(Visible(root, "LedgerChoicePage") && choices.Items.Count == 2 && choices.SelectedItem is null && !open.IsEnabled,
                "家計簿選択UserControlは未選択・開く無効で開始し自動選択しない");
            Check(AutomationProperties.GetAutomationId(choices) == "LedgerChoices" && AutomationProperties.GetAutomationId(open) == "OpenLedgerButton" &&
                choices.SelectionMode == ListViewSelectionMode.Single && choices.DisplayMemberPath == "Name" && choices.MaxHeight == 320 && !page.IsTabStop,
                "元のAutomationId・単一選択・名称表示・高さ・余分なTab停止なしを維持する");
            choices.SelectedIndex = 1;
            Check(open.IsEnabled && notifications.Count == 0 && fixture.Requests.All(p => !p.EndsWith("/open", StringComparison.Ordinal)),
                "選択状態と開くボタンの有効化はUserControl内で扱い、選択だけでは通知・通信しない");
            page.Ledgers = [];
            Check(choices.Items.Count == 0 && choices.SelectedItem is null && !open.IsEnabled && notifications.Count == 0,
                "一覧クリアは選択とボタンを解除し開く要求を発行しない");
            page.Ledgers = data;
            Check(choices.SelectedItem is null && !open.IsEnabled, "一覧を再表示しても元の選択を自動採用しない");
            choices.SelectedIndex = 0; choices.ScrollIntoView(choices.Items[0]);
            await Wait(() => choices.ContainerFromIndex(0) is ListViewItem { IsLoaded: true, ActualWidth: > 0 });
            await ActivateReceiptInputWindowAsync();
            ((ListViewItem)choices.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
            await PressReceiptKey(0x28); await Wait(() => choices.SelectedIndex == 1);
            await PressReceiptKey(0x26); await Wait(() => choices.SelectedIndex == 0);
            Check(open.IsEnabled && notifications.Count == 0, "一覧の実上下キーは選択だけを変更し開く要求を発行しない");
            await PressReceiptKey(0x28); await Wait(() => choices.SelectedIndex == 1);
            await PressReceiptKey(0x09); await Wait(() => TransferControlHasFocus(root, open));
            await PressReceiptKey(0x09, 0x10); await Wait(() => TransferControlHasFocus(root, choices));
            Check(notifications.Count == 0, "Tabで開くへ移動しShift+Tabで一覧へ戻り、フォーカス移動だけでは開かない");
            await PressReceiptKey(0x09); await Wait(() => TransferControlHasFocus(root, open));
            var selected = data[1].LedgerId;
            await PressReceiptKey(0x0D); await Wait(() => Visible(root, "HomePage"));
            Check(notifications.Count == 1 && ReferenceEquals(notifications[0].Sender, page) && notifications[0].Id == selected &&
                fixture.Requests.Count(p => p.EndsWith("/open", StringComparison.Ordinal)) == 1 && Find<TextBlock>(root, "LedgerName").Text == "もう一つの家計簿" && ReferenceEquals(originalWindow, window),
                "実Enterは選択IDを一度通知しMainWindowが一度だけ家計簿を開く。同じWindowを維持する");
        }
        using (var fixture = new SyntheticConnection(saved: true) { ChoiceCount = 2, CannotOpenLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            var page = Find<LedgerChoiceView>(root, "LedgerChoicePage");
            var choices = Find<ListView>(page, "LedgerChoices"); var open = Find<Button>(page, "OpenLedgerButton");
            choices.SelectedIndex = 0;
            await InvokeAsync(() => open); await Wait(() => Equals(TransferRuntime("busy"), false));
            Check(Visible(root, "LedgerChoicePage") && choices.SelectedIndex == 0 && open.IsEnabled && Find<InfoBar>(root, "ErrorNotice").IsOpen,
                "開く処理の失敗はMainWindowで表示しUserControlの選択と再操作可能状態を保持する");
            fixture.RejectSession = fixture.RejectAuthentication = true;
            await InvokeAsync(() => open); await Wait(() => Visible(root, "LoginPage"));
            Check(choices.Items.Count == 0 && choices.SelectedItem is null && !open.IsEnabled && page.Ledgers.Count == 0,
                "認証失効による家計簿解除はUserControlの一覧と選択もクリアする");
        }
    }
}
