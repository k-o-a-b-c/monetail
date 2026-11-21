using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Connection;
using Monetail.WinUI.Views.Authentication;
using Monetail.WinUI.Views.Connection;

namespace Monetail.WinUI.Authentication;

/// <summary>認証前のPage・OAuth待機・読取取消・再試行を、そのフローの寿命内で所有する。</summary>
internal sealed class AuthenticationFlow(
    ClientConnection connection,
    CancellationToken windowLifetime,
    Func<Func<Task>, Action<string>, Task> execute,
    Func<bool> canOperate,
    Func<AuthenticatedAccount, CancellationToken, bool, Task> authenticated,
    Action<string?> loginRequired,
    Func<Task> changeConnection,
    Action<string?> notice,
    Action close) : IDisposable
{
    private Frame? frame = new() { IsNavigationStackEnabled = false };
    private CancellationTokenSource? readCancellation;
    private CancellationTokenSource? browserCancellation;
    private BrowserOAuthLogin? browserLogin;
    private Func<Task>? retry;

    public Frame? View => frame;
    public object? Page => frame?.Content;
    public bool IsStarting => Page is StartupPage;

    public bool PrepareInitialDestination()
    {
        if (frame is null)
            return false;
        try
        {
            if (!connection.LoadSettings())
            {
                ShowConnectionSetup();
                return false;
            }
            if (!connection.Session.HasSavedAuthentication())
            {
                loginRequired(null);
                return false;
            }
            ShowPage(typeof(StartupPage));
            return true;
        }
        catch (AuthenticationContextChangedException error)
        {
            loginRequired(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ShowFailure("設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。", RestartAsync);
        }
        return false;
    }

    private Task RestartAsync() => PrepareInitialDestination() ? StartAsync() : Task.CompletedTask;

    public Task StartAsync() => frame is not null && canOperate() && IsStarting
        ? RunConnectionReadAsync(RestoreAsync, RestartAsync)
        : Task.CompletedTask;

    private Task RunConnectionReadAsync(Func<CancellationToken, Task> operation, Func<Task> repeat)
    {
        var page = Page;
        return execute(async () =>
        {
            using var timeout = ConnectionTimeout();
            readCancellation = timeout;
            try
            {
                await operation(timeout.Token);
            }
            catch (Exception error) when ((frame is null || !ReferenceEquals(Page, page)) && IsNavigationFailure(error))
            {
                // 離脱/終了後の接続・認証応答と失敗で、現在の画面を上書きしない。
            }
            finally
            {
                if (ReferenceEquals(readCancellation, timeout))
                    readCancellation = null;
            }
        }, message => ShowFailure(message, repeat));
    }

    private static bool IsNavigationFailure(Exception error) =>
        error is OperationCanceledException or ApiConnectionException or HttpRequestException or
            AuthenticationRejectedException or AuthenticationContextChangedException or LedgerRequestException or
            IOException or UnauthorizedAccessException or CryptographicException or SocketException;

    // 401復帰は先に元の画面を表示してマスタ取得を続けるため、Page解放だけで中断しない。
    public void KeepRecoveryReadAlive() => readCancellation = null;

    private CancellationTokenSource ConnectionTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime);
        timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
        return timeout;
    }

    private async Task RestoreAsync(CancellationToken token)
    {
        var account = await connection.Session.RestoreAsync(token);
        token.ThrowIfCancellationRequested();
        if (frame is null)
            return;
        if (account is null)
            loginRequired(null);
        else
            await authenticated(account, token, false);
    }

    public Task ConnectAsync(ApiEndpoint candidate, CancellationToken token)
    {
        if (frame is null || !canOperate() || token.IsCancellationRequested)
            return Task.CompletedTask;
        return RunConnectionReadAsync(async readToken =>
        {
            try
            {
                // 検証/保存後は設定Viewの解放と切り離し、認証フロー/Windowの寿命で管理する。
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(readToken);
                using var cancellation = token.Register(timeout.Cancel);
                await connection.ConnectAsync(candidate, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            await RestoreAsync(readToken);
        }, () => ConnectAsync(candidate, windowLifetime));
    }

    public void ShowConnectionSetup()
    {
        if (frame is not null)
            ShowPage(typeof(ConnectionSetupPage), new ConnectionSettingsViewModel(connection.Endpoint, ConnectAsync,
                connection.Endpoint is not null ? RestartAsync : null));
    }

    public void ShowLogin(string? message = null)
    {
        if (frame is null)
            return;
        if (Page is not LoginPage)
            ShowPage(typeof(LoginPage));
        else
            notice(null);
        (Page as LoginPage)?.SetAuthenticationState(false, canOperate());
        if (message is not null)
            notice(message);
    }

    private void ShowFailure(string message, Func<Task> repeat)
    {
        if (frame is null)
            return;
        retry = repeat;
        ShowPage(typeof(FailurePage), message);
    }

    public void SetOperationsEnabled()
    {
        RenderLogin();
        if (Page is FailurePage failure)
            failure.IsEnabled = canOperate();
    }

    private void ShowPage(Type type, object? parameter = null)
    {
        if (frame is null)
            return;
        ReleasePage();
        notice(null);
        frame.Navigate(type, parameter);
        if (Page is LoginPage login)
        {
            login.SetAuthenticationState(false, canOperate());
            login.LoginRequested += Login;
            login.ConnectionChangeRequested += ChangeConnection;
            login.BrowserOpenRequested += ReopenBrowser;
            login.CancelRequested += CancelLogin;
        }
        if (Page is FailurePage failure)
        {
            failure.IsEnabled = canOperate();
            failure.RetryRequested += Retry;
            failure.ConnectionChangeRequested += ChangeConnection;
            failure.ExitRequested += Exit;
        }
    }

    private void ReleasePage()
    {
        readCancellation?.Cancel();
        browserCancellation?.Cancel();
        if (Page is ConnectionSetupPage setup)
            setup.Release();
        if (Page is LoginPage login)
        {
            login.LoginRequested -= Login;
            login.ConnectionChangeRequested -= ChangeConnection;
            login.BrowserOpenRequested -= ReopenBrowser;
            login.CancelRequested -= CancelLogin;
        }
        if (Page is FailurePage failure)
        {
            failure.RetryRequested -= Retry;
            failure.ConnectionChangeRequested -= ChangeConnection;
            failure.ExitRequested -= Exit;
        }
        if (frame is not null)
            frame.Content = null;
    }

    private void RenderLogin() => (Page as LoginPage)?.SetAuthenticationState(browserCancellation is not null,
        browserCancellation is null ? canOperate() : browserLogin is not null && !browserCancellation.IsCancellationRequested);

    private async void Login(object sender, RoutedEventArgs args) => await LoginAsync();

    internal Task LoginAsync()
    {
        if (frame is null || !canOperate())
            return Task.CompletedTask;
        var page = Page;
        return execute(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime);
            browserCancellation = cancellation;
            try
            {
                AuthenticatedAccount? account;
                await using (var login = new BrowserOAuthLogin(connection.Api, connection.Endpoint!))
                {
                    browserLogin = login;
                    RenderLogin();
                    await OpenBrowserAsync();
                    // 利用者のブラウザー操作待ちには10秒の接続期限を適用しない。
                    var acquired = await login.CompleteAsync(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    // URL再表示/利用者取消は止めるが、Page離脱/Window終了の取消は検証完了まで保持する。
                    browserLogin = null;
                    RenderLogin();
                    account = await connection.Session.AcceptLoginAsync(acquired, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                browserLogin = null;
                browserCancellation = null;
                if (frame is null || !ReferenceEquals(Page, page))
                    return;
                if (account is null)
                    loginRequired("ログインを続けられませんでした。もう一度ログインしてください。");
                else
                    await authenticated(account, cancellation.Token, true);
            }
            catch (Exception error) when ((frame is null || !ReferenceEquals(Page, page)) && IsNavigationFailure(error))
            {
                // 旧ログインの完了/失敗を新しいPageへ反映しない。
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !windowLifetime.IsCancellationRequested)
            {
                if (frame is not null && ReferenceEquals(Page, page))
                    loginRequired(null);
            }
            catch (AuthenticationRejectedException)
            {
                if (frame is not null && ReferenceEquals(Page, page))
                    loginRequired("ログインできませんでした。もう一度ログインしてください。");
            }
            finally
            {
                browserLogin = null;
                browserCancellation = null;
                RenderLogin();
            }
        }, message => ShowFailure(message, RestartAsync));
    }

    private async Task OpenBrowserAsync()
    {
        var login = browserLogin;
        if (frame is null || login is null || browserCancellation is not { IsCancellationRequested: false })
            return;
        try
        {
            var opened = await connection.OpenBrowser(login.AuthorizationUri);
            if (frame is null || !ReferenceEquals(browserLogin, login) || browserCancellation is not { IsCancellationRequested: false })
                return;
            if (opened)
            {
                notice(null);
                return;
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
            or System.Runtime.InteropServices.COMException) { }
        if (frame is not null && ReferenceEquals(browserLogin, login) && browserCancellation is { IsCancellationRequested: false })
            notice("ブラウザーを開けませんでした。「ブラウザーを開く」から再試行してください。");
    }

    private async void ReopenBrowser(object sender, RoutedEventArgs args) => await OpenBrowserAsync();
    private void CancelLogin(object sender, RoutedEventArgs args)
    {
        if (browserLogin is not null)
            browserCancellation?.Cancel();
        RenderLogin();
    }
    private async void ChangeConnection(object? sender, RoutedEventArgs? args) => await changeConnection();
    private async void Retry(object sender, RoutedEventArgs args)
    {
        if (frame is not null && canOperate() && retry is { } repeat)
            await repeat();
    }
    private void Exit(object sender, RoutedEventArgs args) => close();

    public void Dispose()
    {
        if (frame is null)
            return;
        browserCancellation?.Cancel();
        ReleasePage();
        frame = null;
        readCancellation = null;
        browserCancellation = null;
        browserLogin = null;
        retry = null;
    }
}
