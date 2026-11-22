using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Management;
using Monetail.WinUI.Views.Workspace;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckManagementLedgerSwitch()
    {
        foreach (var kind in new[] { "Person", "Category", "Payment" })
        {
            using var fixture = new SyntheticConnection(saved: true) { ChoiceCount = 2 };
            var (root, writes) = await OpenSwitchManagementAsync(fixture, kind);
            if (kind == "Person")
            {
                await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
                await WaitPersonEditorReady(root);
                var name = Find<TextBox>(root, "PersonName");
                name.Text = "ダイアログの入力を維持";
                var requests = fixture.Requests.Count;
                CallLedgerMenu(root);
                await CallLedgerOpenAsync(Guid.NewGuid());
                await Settle();
                Check(TransferRuntime("ledgerMenu") is null && fixture.Requests.Count == requests &&
                    name.Text == "ダイアログの入力を維持" && !EditorClosed(root),
                    "管理編集ダイアログ中は家計簿メニュー・直接切替を拒否し入力を保持する");
                await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
                await Wait(() => EditorClosed(root));
            }
            await OpenSwitchOrderAsync(root, kind);
            var page = Find<ManagementPage>(root, "ManagementPage");
            var feature = ManagementFeature(root);
            var list = Find<ListView>(root, kind + "OrderList");
            list.SelectedIndex = 0;
            await InvokeAsync(() => Find<Button>(root, "Move" + kind + "DownButton"));
            var order = list.Items.Cast<object>().ToArray();
            var selected = list.SelectedItem;
            Check(page.ViewModel.HasOrderChanges, kind + "の実際の順序変更を未保存と判定する");
            var opened = fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal));
            var menu = await OpenManagementLedgerMenuAsync(root);
            Check(TransferRuntime("ledgerSwitchDialog") is null && ReferenceEquals(feature, ManagementFeature(root)) &&
                list.Items.Cast<object>().SequenceEqual(order) && ReferenceEquals(selected, list.SelectedItem),
                kind + "はメニューを開くだけでは確認・画面破棄・順序変更を行わない");
            await ActivateReceiptInputWindowAsync();
            await PressReceiptKey(0x1B);
            await Wait(() => !menu.IsOpen && TransferRuntime("ledgerMenu") is null);
            Check(ReferenceEquals(feature, ManagementFeature(root)) && list.Items.Cast<object>().SequenceEqual(order) &&
                ReferenceEquals(selected, list.SelectedItem) && writes() == 0,
                kind + "のメニュー取消は同じ画面・並び順・選択を完全に維持する");
            menu = await OpenManagementLedgerMenuAsync(root);
            await ChooseManagementLedgerMenuItemAsync(menu, "家族の家計簿");
            await Wait(() => !menu.IsOpen);
            Check(TransferRuntime("ledgerSwitchDialog") is null && page.ViewModel.HasOrderChanges &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == opened,
                kind + "は現在の家計簿を選んでも未保存順を破棄・再オープンしない");
            menu = await OpenManagementLedgerMenuAsync(root);
            await ChooseManagementLedgerMenuItemAsync(menu, "もう一つの家計簿");
            var confirmation = await LedgerSwitchConfirmationAsync();
            Check(confirmation.DefaultButton == ContentDialogButton.Close && confirmation.PrimaryButtonText == "破棄して切替" &&
                confirmation.CloseButtonText == "キャンセル", kind + "は別家計簿の選択時だけ破棄確認を表示する");
            var section = page.ViewModel.SelectedSection;
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await CallLedgerOpenAsync(Guid.NewGuid());
            Check(page.ViewModel.SelectedSection == section && ReferenceEquals(feature, ManagementFeature(root)) &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == opened,
                kind + "の確認中は別の管理遷移・家計簿切替を受け付けない");
            await InvokeAsync(() => LedgerSwitchFooter(confirmation, false));
            await Wait(() => TransferRuntime("ledgerSwitchDialog") is null);
            Check(ReferenceEquals(feature, ManagementFeature(root)) && list.Items.Cast<object>().SequenceEqual(order) &&
                ReferenceEquals(selected, list.SelectedItem) && page.ViewModel.HasOrderChanges && writes() == 0,
                kind + "の破棄確認取消は画面・順序・選択・未保存判定を維持し保存しない");
            if (kind == "Person")
            {
                menu = await OpenManagementLedgerMenuAsync(root);
                await ChooseManagementLedgerMenuItemAsync(menu, "＋追加");
                confirmation = await LedgerSwitchConfirmationAsync();
                await InvokeAsync(() => LedgerSwitchFooter(confirmation, false));
                await Wait(() => TransferRuntime("ledgerSwitchDialog") is null);
                Check(CreationScreen(root) is null && ReferenceEquals(feature, ManagementFeature(root)) &&
                    list.Items.Cast<object>().SequenceEqual(order) && ReferenceEquals(selected, list.SelectedItem),
                    "家計簿追加画面への移動でも確認取消で未保存順を暗黙に破棄しない");
            }
            if (kind == "Person")
            {
                fixture.FailLedgerSwitch = true;
                menu = await OpenManagementLedgerMenuAsync(root);
                await ChooseManagementLedgerMenuItemAsync(menu, "もう一つの家計簿");
                confirmation = await LedgerSwitchConfirmationAsync();
                await InvokeAsync(() => LedgerSwitchFooter(confirmation, true));
                await Wait(() => TransferRuntime("ledgerSwitchDialog") is null && Find<InfoBar>(root, "ErrorNotice").IsOpen);
                Check(ReferenceEquals(feature, ManagementFeature(root)) && list.Items.Cast<object>().SequenceEqual(order) &&
                    ReferenceEquals(selected, list.SelectedItem) && page.ViewModel.HasOrderChanges && writes() == 0,
                    "切替APIの失敗では破棄を確定せず、元の未保存順と選択を維持する");
                fixture.FailLedgerSwitch = false;
                opened = fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal));
            }
            menu = await OpenManagementLedgerMenuAsync(root);
            await ChooseManagementLedgerMenuItemAsync(menu, "もう一つの家計簿");
            confirmation = await LedgerSwitchConfirmationAsync();
            await InvokeAsync(() => LedgerSwitchFooter(confirmation, true));
            await Wait(() => Visible(root, "HomePage") && Find<TextBlock>(root, "LedgerName").Text == "もう一つの家計簿");
            Check(writes() == 0 && page.ViewModel.Context.IsDisposed && !page.ViewModel.HasOrderChanges &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == opened + 1,
                kind + "は明示的な破棄承認後だけ切替・管理状態解放を行い自動保存しない");
        }
        foreach (var kind in new[] { "Person", "Category", "Payment" })
        {
            using var fixture = new SyntheticConnection(saved: true) { ChoiceCount = 2 };
            var (root, writes) = await OpenSwitchManagementAsync(fixture, kind);
            await OpenSwitchOrderAsync(root, kind);
            var page = Find<ManagementPage>(root, "ManagementPage");
            Check(!page.ViewModel.HasOrderChanges, kind + "は並び替え画面を開いただけでは未保存にしない");
            var list = Find<ListView>(root, kind + "OrderList");
            list.SelectedIndex = 0;
            await InvokeAsync(() => Find<Button>(root, "Move" + kind + "DownButton"));
            await InvokeAsync(() => Find<Button>(root, "Move" + kind + "UpButton"));
            Check(!page.ViewModel.HasOrderChanges, kind + "は元の順序に戻すと未保存を解除する");
            var menu = await OpenManagementLedgerMenuAsync(root);
            await ChooseManagementLedgerMenuItemAsync(menu, "もう一つの家計簿");
            await Wait(() => Visible(root, "HomePage"));
            Check(TransferRuntime("ledgerSwitchDialog") is null && writes() == 0 &&
                Find<TextBlock>(root, "LedgerName").Text == "もう一つの家計簿",
                kind + "は実順序が未変更なら確認せず切り替え、自動保存しない");
        }
        await CheckManagementLedgerMenuLifetime();
    }

    private async Task CheckManagementLedgerMenuLifetime()
    {
        using var fixture = new SyntheticConnection(saved: true) { ChoiceCount = 2 };
        var (root, _) = await OpenSwitchManagementAsync(fixture, "Person");
        fixture.LedgerListGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = fixture.Requests.Count(path => path == "/ledgers");
        await InvokeAsync(() => Find<Button>(root, "LedgerSelector"));
        await Wait(() => Equals(TransferRuntime("busy"), true));
        CallLedgerMenu(root);
        await CallLedgerOpenAsync(Guid.NewGuid());
        Check(!Find<Button>(root, "LedgerSelector").IsEnabled && fixture.Requests.Count(path => path == "/ledgers") == reads + 1,
            "家計簿取得中は共通busy制御で二重取得・別家計簿オープンを禁止する");
        var navigate = typeof(MainWindow).GetMethod("NavigateWorkspace", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var screen = typeof(MainWindow).GetNestedType("WorkspaceScreen", BindingFlags.NonPublic)!;
        navigate.Invoke(window, [Enum.Parse(screen, "HomePage"), null]);
        fixture.LedgerListGate.SetResult(true);
        await Wait(() => Equals(TransferRuntime("busy"), false));
        Check(Visible(root, "HomePage") && TransferRuntime("ledgerMenu") is null,
            "取得元の管理Pageを離れた後の応答は新しい画面に家計簿メニューを表示しない");
    }

    private async Task<(FrameworkElement Root, Func<int> Writes)> OpenSwitchManagementAsync(SyntheticConnection fixture, string kind)
    {
        switch (kind)
        {
            case "Category":
                var categories = new CategoryServer();
                return (await OpenCategoriesWindow(fixture, categories), () => categories.Writes.Count);
            case "Payment":
                var payments = new PaymentServer();
                return (await OpenPaymentsWindow(fixture, payments), () => payments.Writes.Count);
            default:
                var people = new PeopleServer();
                return (await OpenPersonWindow(fixture, people), () => people.Writes.Count);
        }
    }

    private async Task OpenSwitchOrderAsync(FrameworkElement root, string kind)
    {
        if (kind == "Payment")
        {
            var page = (PaymentMethodsPage)ManagementFeature(root)!;
            var owner = page.ViewModel.Info?.MyPersonId ?? throw new InvalidOperationException("本人対応がありません。");
            await OpenOrderFor(root, owner);
        }
        else if (kind == "Category")
            await OpenCategoryOrderScreen(root);
        else
        {
            await InvokeAsync(() => Find<Button>(root, "PersonOrderButton"));
            await Wait(() => ManagementVisible(root, "PersonOrderContent"));
        }
    }

    private void CallLedgerMenu(FrameworkElement root) => typeof(MainWindow)
        .GetMethod("ShowLedgerMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(window, [Find<Button>(root, "LedgerSelector"), new RoutedEventArgs()]);

    private Task CallLedgerOpenAsync(Guid id) => (Task)typeof(MainWindow)
        .GetMethod("OpenLedgerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [id])!;

    private async Task<MenuFlyout> OpenManagementLedgerMenuAsync(FrameworkElement root)
    {
        await InvokeAsync(() => Find<Button>(root, "LedgerSelector"));
        await Wait(() => TransferRuntime("ledgerMenu") is MenuFlyout { IsOpen: true });
        return (MenuFlyout)TransferRuntime("ledgerMenu")!;
    }

    private async Task ChooseManagementLedgerMenuItemAsync(MenuFlyout menu, string name)
    {
        var item = menu.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == name);
        await ActivateReceiptInputWindowAsync();
        await FocusTransferControl(item);
        await PressReceiptKey(0x0D);
    }

    private async Task<ContentDialog> LedgerSwitchConfirmationAsync()
    {
        await Wait(() => TransferRuntime("ledgerSwitchDialog") is ContentDialog { IsLoaded: true });
        var dialog = (ContentDialog)TransferRuntime("ledgerSwitchDialog")!;
        ObserveDialog(dialog);
        return dialog;
    }

    private static Button LedgerSwitchFooter(ContentDialog dialog, bool primary) => All(dialog).OfType<Button>()
        .First(button => button.IsLoaded && Equals(button.Content, primary ? dialog.PrimaryButtonText : dialog.CloseButtonText));
}
