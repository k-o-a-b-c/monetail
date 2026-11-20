using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Connection;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckConnectionSettingsLifecycle()
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture, (_, initial) =>
                Check(Startup(initial) is not null && fixture.Requests.Count == 0 &&
                    Find<ContentControl>(initial, "ConnectionSettingsContent").Content is null,
                    "設定済み起動はActivate前に行き先を決め、初回設定Viewや通信を先行生成しない"));
            await StartSyntheticAsync();
            await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 1);
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "ConnectionNavigation");
            await Settle();
            var first = ConnectionView(root);
            var model = first.ViewModel;
            Check(Find<ContentControl>(root, "ConnectionSettingsContent").Content is ConnectionSettingsView &&
                !model.CanCancel && Find<ContentControl>(root, "AuthenticationHost").Content is null,
                "管理画面も共通UserControlだけを必要時に生成し初回Pageを併存させない");
            var input = Find<TextBox>(root, "ServerUrl");
            input.Text = "http://invalid.example.test/";
            Check(model.ServerUrl == input.Text, "URL入力はPropertyChangedの双方向バインドでVMへ一元反映する");
            var reads = fixture.Requests.Count;
            await FocusTransferControl(Find<Button>(root, "ConnectButton"));
            await InvokeAsync(() => Find<Button>(root, "ConnectButton"));
            Check(model.UrlError is not null && All(first).OfType<TextBlock>().Any(block => block.Text == model.UrlError && VisibleAncestors(block)) &&
                ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), input) &&
                fixture.Requests.Count == reads && fixture.Tokens.Current is not null &&
                fixture.Settings.Load()!.Equals(SyntheticConnection.Endpoint) && input.Text == model.ServerUrl,
                "管理の不正URLは近傍検証で入力/設定/認証を保持しネットワークへ送らない");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await Settle();
            Check(Find<ContentControl>(root, "ConnectionSettingsContent").Content is null && !model.ConnectCommand.CanExecute(null),
                "通常離脱で共通Viewを解放し古いVMの操作を停止する");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "ConnectionNavigation");
            await Settle();
            Check(!ReferenceEquals(first, ConnectionView(root)) && !ReferenceEquals(model, ConnectionView(root).ViewModel) &&
                Find<TextBox>(root, "ServerUrl").Text == SyntheticConnection.Endpoint.Value,
                "再訪は保存済みURLから新しいView/VMを生成し未保存の前画面をキャッシュしない");
            input = Find<TextBox>(root, "ServerUrl");
            await ActivateReceiptInputWindowAsync();
            await FocusTransferControl(input);
            await PressReceiptKey(0x0D);
            await Wait(() => ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), Find<Button>(root, "ConnectButton")));
            Check(fixture.Requests.Count == reads && fixture.Settings.Load()!.Equals(SyntheticConnection.Endpoint),
                "URL欄のEnterは接続ボタンへ移るだけで接続/保存しない");
            model = ConnectionView(root).ViewModel;
            input.Text = "https://other.example.test/";
            fixture.FailConnection = true;
            await InvokeAsync(() => Find<Button>(root, "ConnectButton"));
            await Wait(() => Visible(root, "FailurePage"));
            Check(fixture.Settings.Load()!.Equals(SyntheticConnection.Endpoint) && fixture.Tokens.Current is not null &&
                !fixture.OtherServerAuthenticated && !model.ConnectCommand.CanExecute(null) && model.ServerUrl == input.Text,
                "接続失敗は既存の再試行画面へ進み、元設定/認証/送信入力を維持し旧Viewを解放する");
            fixture.FailConnection = false;
            await InvokeAsync(() => Find<Button>(root, "RetryButton"));
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Settings.Load()!.Value == "https://other.example.test/" && fixture.Tokens.Current is null &&
                !fixture.OtherServerAuthenticated && fixture.OtherServerRequests.Count == 2,
                "明示再試行は同じ候補で確認後だけ保存し、元トークンを変更先へ送らない");
            var change = Find<HyperlinkButton>(root, "LoginChangeConnection");
            await FocusTransferControl(change);
            await PressReceiptKey(0x0D);
            await Wait(() => ConnectionSetupVisible(root));
            var setup = ConnectionView(root);
            Check(setup.GetType() == first.GetType() && setup.ViewModel.CanCancel,
                "初回Pageと管理は同じUserControl型を使い、保存済み接続先からの変更にはキャンセルを示す");
            Find<TextBox>(root, "ServerUrl").Text = "https://discard.example.test/";
            await InvokeAsync(() => Find<Button>(root, "ConnectionCancel"));
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Settings.Load()!.Value == "https://other.example.test/" &&
                LoginScreen(root) is not null && !setup.ViewModel.ConnectCommand.CanExecute(null),
                "キャンセルは未保存URLを反映せず保存先へ戻り、初回Page/VMを解放する");
        }
        using (var fixture = new SyntheticConnection())
        {
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var root = await OpenSyntheticWindow(fixture);
            var view = ConnectionView(root);
            view.ViewModel.ServerUrl = SyntheticConnection.Endpoint.Value;
            Check(Find<TextBox>(root, "ServerUrl").Text == view.ViewModel.ServerUrl,
                "プログラムからのURL通知も同じ入力欄へ反映する");
            await InvokeAsync(() => Find<Button>(root, "ConnectButton"));
            await Wait(() => fixture.Requests.Count == 1);
            var pending = view.ViewModel.ConnectCommand.ExecutionTask ?? throw new InvalidOperationException("接続要求がありません。");
            Check(view.ViewModel.IsConnecting && !Find<TextBox>(root, "ServerUrl").IsEnabled &&
                !Find<Button>(root, "ConnectButton").IsEnabled && fixture.Settings.Load() is null &&
                Find<ProgressRing>(root, "BusyProgress").IsActive && Find<ProgressRing>(root, "BusyProgress").IsEnabled &&
                ((Grid)root).Children.IndexOf(Find<ProgressRing>(root, "BusyProgress")) >
                    ((Grid)root).Children.IndexOf(Find<ContentControl>(root, "AuthenticationHost")),
                "接続中の処理状態とCommand可否を通知し、確認前に設定を保存しない");
            using var next = new SyntheticConnection();
            await OpenSyntheticWindow(next);
            await pending;
            Check(fixture.ConnectionCancelled && fixture.Settings.Load() is null &&
                !view.ViewModel.ConnectCommand.CanExecute(null),
                "Window終了で進行中の確認を中断し、遅い結果から設定を保存しない");
        }
    }
}
