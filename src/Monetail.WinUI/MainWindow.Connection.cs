using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private readonly ClientConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? browserCancellation;
    private BrowserOAuthLogin? browserLogin;
    private AuthenticatedAccount? account;
    private Func<Task>? retry;
    private bool busy;
    private bool closed;

    internal Task StartAsync()
    {
        if (busy || closed) return Task.CompletedTask;
        ShowPage(StartupPage);
        return RunAsync(async () =>
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var notice = ShowStartupNoticeAsync(waiting.Token);
            try
            {
                if (!connection.LoadSettings()) { ShowConnection(); return; }
                using var timeout = ConnectionTimeout();
                await connection.Api.CheckConnectionAsync(timeout.Token);
                await RestoreAndOpenAsync(timeout.Token);
            }
            finally
            {
                // 完了・失敗・終了の後に、遅れて待機表示を出さない。
                waiting.Cancel();
                await notice;
                if (!closed)
                {
                    StartupNotice.Visibility = Visibility.Collapsed;
                    StartupProgress.IsActive = false;
                }
            }
        }, StartAsync, true);
    }

    private async Task ShowStartupNoticeAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            if (!closed && StartupPage.Visibility == Visibility.Visible)
            {
                StartupNotice.Visibility = Visibility.Visible;
                StartupProgress.IsActive = true;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private CancellationTokenSource ConnectionTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
        return timeout;
    }

    private async Task RestoreAndOpenAsync(CancellationToken token)
    {
        account = await connection.Session.RestoreAsync(token);
        if (account is null) ShowLogin();
        else await OpenDestinationAsync(token);
    }

    private void ShowConnection(object? sender = null, RoutedEventArgs? args = null)
    {
        DropPendingPersonEdit();
        if (account is not null && currentLedger is not null)
        {
            ShowPage(ManagementPage);
            PeopleContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
            ConnectionSettingsContent.Visibility = Visibility.Visible;
            SettingsServerUrl.Text = connection.Endpoint?.Value ?? "";
            ManagementNavigation.SelectedItem = ConnectionNavigation;
            return;
        }
        ShowPage(ConnectionPage);
        ServerUrl.Text = connection.Endpoint?.Value ?? "";
        ConnectionCancel.Visibility = connection.Endpoint is null ? Visibility.Collapsed : Visibility.Visible;
        ServerUrl.Focus(FocusState.Programmatic);
    }

    private async void Connect(object sender, RoutedEventArgs args)
    {
        ApiEndpoint candidate;
        try { candidate = ApiEndpoint.Parse(ServerUrl.Text); }
        catch (FormatException error) { ShowError(error.Message); ServerUrl.Focus(FocusState.Programmatic); return; }
        await ConnectAsync(candidate);
    }

    private Task ConnectAsync(ApiEndpoint candidate) => RunAsync(async () =>
    {
        using var timeout = ConnectionTimeout();
        await connection.ConnectAsync(candidate, timeout.Token);
        await RestoreAndOpenAsync(timeout.Token);
    }, () => ConnectAsync(candidate), true);

    private async void CancelConnection(object sender, RoutedEventArgs args) => await StartAsync();

    private void ShowLogin(string? message = null)
    {
        account = null;
        AccountNavigation.IsEnabled = LogoutNavigation.IsEnabled = false;
        ClearLedger();
        ShowPage(LoginPage);
        if (message is not null) ShowError(message);
    }

    private async void Login(object sender, RoutedEventArgs args) => await LoginAsync();

    private Task LoginAsync() => RunAsync(async () =>
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        browserCancellation = cancellation;
        try
        {
            await using var login = new BrowserOAuthLogin(connection.Api, connection.Endpoint!);
            browserLogin = login;
            ShowPage(BrowserPage);
            await OpenBrowserAsync();
            // 利用者の認証待ちには接続確認用の10秒制限をかけない。
            var acquired = await login.CompleteAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ReopenBrowserButton.IsEnabled = CancelLoginButton.IsEnabled = false;
            account = await connection.Session.AcceptLoginAsync(acquired, cancellation.Token);
            if (account is null) ShowLogin("ログインを続けられませんでした。もう一度ログインしてください。");
            else await OpenDestinationAsync(cancellation.Token);
            if (!closed) BringToFront();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !closed) { ShowLogin(); }
        catch (AuthenticationRejectedException) { ShowLogin("ログインできませんでした。もう一度ログインしてください。"); }
        finally { browserLogin = null; browserCancellation = null; ReopenBrowserButton.IsEnabled = CancelLoginButton.IsEnabled = true; }
    }, StartAsync, true);

    private async Task OpenBrowserAsync()
    {
        if (browserLogin is null) return;
        try
        {
            if (await connection.OpenBrowser(browserLogin.AuthorizationUri))
            {
                ErrorNotice.IsOpen = false;
                ErrorNotice.Visibility = Visibility.Collapsed;
                return;
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
            or System.Runtime.InteropServices.COMException) { }
        if (!closed) ShowError("ブラウザーを開けませんでした。「ブラウザーを開く」から再試行してください。");
    }

    private async void ReopenBrowser(object sender, RoutedEventArgs args) => await OpenBrowserAsync();
    private void CancelLogin(object sender, RoutedEventArgs args) => browserCancellation?.Cancel();

    private Task LogoutAsync() => RunAsync(async () =>
    {
        DropPendingPersonEdit();
        await connection.Session.LogoutAsync(lifetime.Token);
        ShowLogin();
    }, LogoutAsync);

    private async Task RunAsync(Func<Task> action, Func<Task> repeat, bool connectionFailure = false)
    {
        if (busy || closed) return;
        busy = true;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        SetOperationsEnabled(false);
        try { await action(); }
        catch (OperationCanceledException) when (closed) { }
        catch (ObjectDisposedException) when (closed) { }
        catch (AuthenticationRejectedException)
        {
            RememberPersonEdit();
            ShowLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (AuthenticationContextChangedException error) { DropPendingPersonEdit(); ShowLogin(error.Message); }
        catch (LedgerRequestException error)
        {
            var message = error.StatusCode switch
            {
                HttpStatusCode.NotFound => "この家計簿は開けません。参加状態を確認してください。",
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => "家計簿の操作を完了できませんでした。内容を確認して再試行してください。"
            };
            ReportFailure(message, repeat, connectionFailure);
        }
        catch (PeopleRequestException error)
        {
            ApplyPeopleFailureContext(error);
            ReportFailure(error.StatusCode == HttpStatusCode.TooManyRequests
                ? "操作回数の上限に達しました。しばらく待って再試行してください。"
                : "人物一覧を取得できませんでした。家計簿への参加状態と権限を確認してください。", repeat, connectionFailure);
        }
        catch (Exception error) when (error is ApiConnectionException or HttpRequestException or OperationCanceledException
            or IOException or UnauthorizedAccessException or CryptographicException or SocketException)
        {
            var message = error is IOException or UnauthorizedAccessException or CryptographicException
                ? "設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。"
                : "サーバーに接続できませんでした。接続先と通信状態を確認してください。";
            ReportFailure(message, repeat, connectionFailure);
        }
        finally
        {
            busy = false;
            if (!closed) SetOperationsEnabled(true);
        }
    }

    private void ReportFailure(string message, Func<Task> repeat, bool connectionFailure)
    {
        if (!connectionFailure) { ShowError(message); return; }
        ShowPage(FailurePage);
        FailureMessage.Text = message;
        retry = repeat;
    }

    private void SetOperationsEnabled(bool enabled)
    {
        Operations.IsEnabled = enabled;
        PersonEditorContent.IsEnabled = enabled;
        PersonDialog.IsCloseButtonEnabled = enabled;
        UpdatePeopleActions();
        var showProgress = !enabled && StartupPage.Visibility != Visibility.Visible;
        BusyProgress.IsActive = showProgress;
        BusyProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var candidate in new FrameworkElement[] { HomePage, ManagementPage, ConnectionPage, LoginPage,
            BrowserPage, FailurePage, LedgerChoicePage, LedgerCreatePage, StartupPage })
            candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        if (personDialogOpen && !personDialogClosing && !forcePersonDialogClose)
        {
            ShowPersonError(null, message);
            return;
        }
        ErrorNotice.Message = message;
        ErrorNotice.Visibility = Visibility.Visible;
        ErrorNotice.IsOpen = true;
    }

    private void CloseError(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!sender.IsOpen) sender.Visibility = Visibility.Collapsed;
    }

    private async void RetryConnection(object sender, RoutedEventArgs args)
    {
        if (retry is not null) await retry();
    }

    private void Exit(object sender, RoutedEventArgs args) => Close();

    private void CloseConnection(object sender, WindowEventArgs args)
    {
        closed = true;
        DiscardPersonEditor();
        lifetime.Cancel();
        browserCancellation?.Cancel();
        connection.Dispose();
    }
}
