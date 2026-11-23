using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Connection;
using Monetail.WinUI.Views.Connection;
using Monetail.WinUI.Authentication;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private readonly ClientConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private AuthenticatedAccount? account;
    private bool busy;
    private bool closed;

    private AuthenticationFlow? authentication;

    private AuthenticationFlow GetAuthentication() => authentication ??= new AuthenticationFlow(
        connection, lifetime.Token, RunAuthenticationAsync, () => !busy && !closed,
        AuthenticatedAsync, NavigateToLogin, ShowConnectionAsync, AuthenticationNotice, Close);

    private AuthenticationFlow? ShowAuthentication()
    {
        if (closed || RecordEditorActive && !EditorAwaitingLogin)
            return null;
        ReleaseLedgerPage();
        HideWorkspace();
        var flow = GetAuthentication();
        AuthenticationHost.Content = flow.View;
        AuthenticationHost.Visibility = Visibility.Visible;
        return flow;
    }

    private void ReleaseAuthentication()
    {
        var flow = authentication;
        authentication = null;
        AuthenticationHost.Content = null;
        AuthenticationHost.Visibility = Visibility.Collapsed;
        flow?.Dispose();
    }

    private object? CurrentPage => authentication?.Page ?? (LedgerPageHost.Content as Frame)?.Content ?? WorkspaceHost.Content;

    internal Task StartAsync() => authentication?.StartAsync() ?? Task.CompletedTask;
    private Task RunAuthenticationAsync(Func<Task> action, Action<string> failed) => RunAsync(action, message =>
    {
        if (ShowAuthentication() is not null)
            failed(message);
    });

    private async Task AuthenticatedAsync(AuthenticatedAccount authenticated, CancellationToken token, bool fromBrowser)
    {
        token.ThrowIfCancellationRequested();
        account = authenticated;
        if (management is not null)
            management.AccountName = authenticated.DisplayName ?? "";
        if (EditorAwaitingLogin)
            authentication?.KeepRecoveryReadAlive();
        await OpenDestinationAsync(token);
        if (fromBrowser && !closed)
            BringToFront();
    }

    private void AuthenticationNotice(string? message)
    {
        if (message is not null)
            ShowError(message);
        else
        {
            ErrorNotice.IsOpen = false;
            ErrorNotice.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowConnectionAsync()
    {
        if (!EditorAwaitingLogin && !await LeaveRecordEditorAsync()) return;
        if (account is not null && currentLedger is not null)
        {
            NavigateWorkspace(WorkspaceScreen.ManagementPage);
            if (ManagementPage is { } page)
                await page.OpenSectionAsync(Monetail.Windows.Presentation.Workspace.ManagementSection.Connection);
            return;
        }
        OpenConnectionSetup();
    }

    private void OpenConnectionSetup() => ShowAuthentication()?.ShowConnectionSetup();

    private async Task ConnectAsync(ApiEndpoint candidate, CancellationToken token)
    {
        if (!EditorAwaitingLogin && !await LeaveRecordEditorAsync()) return;
        if (!closed && !token.IsCancellationRequested)
            await GetAuthentication().ConnectAsync(candidate, token);
    }

    private bool EditorAwaitingLogin => TransferPage?.ViewModel?.AwaitingLogin == true || ReceiptPage?.ViewModel?.AwaitingLogin == true || management?.Context.IsSuspended == true;

    // ログインへの移動準備。画面表示だけでは編集状態を破棄しない。
    private void NavigateToLogin(string? message = null)
    {
        if (closed)
            return;
        var suspended = SuspendTransferForAuthentication() || SuspendReceiptForAuthentication() || SuspendManagementForAuthentication();
        account = null;
        if (!suspended)
        {
            DiscardLedgerContents();
            ClearLedger();
        }
        ShowLogin(message);
    }

    private void ShowLogin(string? message = null)
    {
        if (closed || RecordEditorActive && !EditorAwaitingLogin)
            return;
        ShowAuthentication()?.ShowLogin(message);
    }

    private async Task LogoutAsync()
    {
        if (!await LeaveRecordEditorAsync())
            return;
        ReturnFromReleasedEditor();
        await RunAsync(async () =>
    {
        await connection.Session.LogoutAsync(lifetime.Token);
        account = null;
        DiscardLedgerContents();
        ClearLedger();
        ShowLogin();
    });
    }

    // 例外分類・busy・認証退避は共通。認証側だけが失敗の表示先を渡す。
    private async Task RunAsync(Func<Task> action, Action<string>? failed = null)
    {
        if (busy || closed)
            return;
        Action<string> report = failed ?? ShowError;
        RememberReceiptAuthenticationView();
        busy = true;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        SetOperationsEnabled(false);
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (closed) { }
        catch (ObjectDisposedException) when (closed) { }
        catch (AuthenticationRejectedException)
        {
            NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (AuthenticationContextChangedException error)
        {
            DiscardManagement();
            NavigateToLogin(error.Message);
        }
        catch (LedgerRequestException error)
        {
            var message = error.StatusCode switch
            {
                HttpStatusCode.NotFound => "この家計簿は開けません。参加状態を確認してください。",
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => "家計簿の操作を完了できませんでした。内容を確認して再試行してください。"
            };
            report(message);
        }
        catch (CategoryRequestException)
        {
            report("科目一覧を取得できませんでした。参加状態と権限を確認してください。");
        }
        catch (PaymentMethodRequestException)
        {
            report("支払方法一覧を取得できませんでした。参加状態と権限を確認してください。");
        }
        catch (PeopleRequestException error)
        {
            report(error.StatusCode == HttpStatusCode.TooManyRequests
                ? "操作回数の上限に達しました。しばらく待って再試行してください。"
                : "人物一覧を取得できませんでした。家計簿への参加状態と権限を確認してください。");
        }
        catch (Exception error) when (error is ApiConnectionException or HttpRequestException or OperationCanceledException
            or IOException or UnauthorizedAccessException or CryptographicException or SocketException)
        {
            var message = error is IOException or UnauthorizedAccessException or CryptographicException
                ? "設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。"
                : "サーバーに接続できませんでした。接続先と通信状態を確認してください。";
            report(message);
        }
        finally
        {
            busy = false;
            if (ReceiptPage?.ViewModel?.IsReadOnly == false)
                ReceiptPage.ForgetPosition();
            if (!closed)
                SetOperationsEnabled(true);
        }
    }

    private void SetOperationsEnabled(bool enabled)
    {
        Operations.IsEnabled = enabled;
        if (CreationView is { } creation)
            creation.IsEnabled = enabled;
        authentication?.SetOperationsEnabled();
        if (management is not null)
            management.Context.IsBusy = !enabled;
        var showProgress = !enabled && authentication?.IsStarting != true;
        BusyProgress.IsActive = showProgress;
        BusyProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SuspendRecordReads()
    {
        CancelReceiptOpening();
        CancelTransferOpening();
        if (RecordPage is { } page)
            page.Suspend();
        else
            recordHome?.Suspend();
    }

    private void ShowError(string message)
    {
        ErrorNotice.Message = message;
        ErrorNotice.Visibility = Visibility.Visible;
        ErrorNotice.IsOpen = true;
    }

    private void CloseError(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!sender.IsOpen) sender.Visibility = Visibility.Collapsed;
    }

    private void CloseConnection(object sender, WindowEventArgs args)
    {
        closed = true;
        AppWindow.Closing -= ConfirmWindowClosing;
        RecordPage?.Suspend();
        ReleaseAuthentication();
        ReleaseLedgerPage();
        ResetTransferEditor();
        ResetReceiptEditor();
        ResetRecordHome();
        ReleaseWorkspace();
        connection.Masters.Changed -= ReceiptMasterChanged;
        DiscardManagement();
        lifetime.Cancel();
        connection.Dispose();
    }
}
