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

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private readonly ClientConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? browserCancellation;
    private CancellationTokenSource? startupCancellation;
    private BrowserOAuthLogin? browserLogin;
    private AuthenticatedAccount? account;
    private Func<Task>? retry;
    private bool busy;
    private bool closed;

    // 接続先と保存情報の有無だけをActivate前に調べる。有効性の判断には通信が必要。
    private bool PrepareInitialDestination()
    {
        try
        {
            if (!connection.LoadSettings())
            {
                OpenConnectionSetup();
                return false;
            }
            if (!connection.Session.HasSavedAuthentication())
            {
                NavigateToLogin();
                return false;
            }
            OpenConnectionPage(typeof(StartupPage));
            return true;
        }
        catch (AuthenticationContextChangedException error)
        {
            NavigateToLogin(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ReportFailure("設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。", RetryStartupAsync, true);
        }
        return false;
    }

    private Task RetryStartupAsync() => PrepareInitialDestination() ? StartAsync() : Task.CompletedTask;

    private bool IsStartupVisible => ConnectionSetupHost.Content is Frame { Content: StartupPage };

    internal Task StartAsync()
    {
        if (busy || closed || !IsStartupVisible)
            return Task.CompletedTask;
        var frame = ConnectionSetupHost.Content;
        return RunAsync(async () =>
        {
            using var timeout = ConnectionTimeout();
            startupCancellation = timeout;
            try
            {
                // 接続先確定時のメタデータ確認は繰り返さず、既存の認証検証で疎通も確認する。
                await RestoreAndOpenAsync(timeout.Token);
            }
            catch (Exception error) when ((closed || !ReferenceEquals(ConnectionSetupHost.Content, frame)) &&
                error is OperationCanceledException or ApiConnectionException or HttpRequestException or
                    AuthenticationRejectedException or AuthenticationContextChangedException or LedgerRequestException or
                    IOException or UnauthorizedAccessException or CryptographicException or SocketException)
            {
                // 退避先へ遷移した後の遅延応答・失敗で画面を横取りしない。
            }
            finally
            {
                if (ReferenceEquals(startupCancellation, timeout))
                    startupCancellation = null;
            }
        }, RetryStartupAsync, true);
    }

    private CancellationTokenSource ConnectionTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
        return timeout;
    }

    private async Task RestoreAndOpenAsync(CancellationToken token)
    {
        var restored = await connection.Session.RestoreAsync(token);
        token.ThrowIfCancellationRequested();
        account = restored;
        if (account is null) NavigateToLogin();
        else
        {
            // 401復帰では編集画面を先に表示し、Window所有のマスタ再取得を続ける。
            // 起動Pageの解放では中断せず、元の10秒期限・Window終了には従う。
            if (EditorAwaitingLogin)
                startupCancellation = null;
            await OpenDestinationAsync(token);
        }
    }

    private async void ShowConnection(object? sender = null, RoutedEventArgs? args = null)
    {
        if (!EditorAwaitingLogin && !await LeaveRecordEditorAsync()) return;
        DropPendingCategoryEdit();
        DropPendingPaymentEdit();
        DropPendingPersonEdit();
        if (account is not null && currentLedger is not null)
        {
            ShowPage(ManagementPage);
            PeopleContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
            var selected = ReferenceEquals(ManagementNavigation.SelectedItem, ConnectionNavigation);
            ManagementNavigation.SelectedItem = ConnectionNavigation;
            if (selected)
                OpenConnectionSettings();
            return;
        }
        OpenConnectionSetup();
    }

    private ConnectionSettingsViewModel CreateConnectionSettings(bool setup) => new(connection.Endpoint, ConnectAsync,
        setup && connection.Endpoint is not null ? RetryStartupAsync : null);

    private void OpenConnectionSetup() => OpenConnectionPage(typeof(ConnectionSetupPage), CreateConnectionSettings(true));

    private void OpenConnectionPage(Type pageType, object? parameter = null)
    {
        ReleaseConnectionSetup();
        ReleaseConnectionSettings();
        LegacyContent.Visibility = Visibility.Collapsed;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        var frame = new Frame { IsNavigationStackEnabled = false };
        ConnectionSetupHost.Content = frame;
        ConnectionSetupHost.Visibility = Visibility.Visible;
        frame.Navigate(pageType, parameter);
    }

    private void OpenConnectionSettings()
    {
        ReleaseConnectionSettings();
        ConnectionSettingsContent.Content = new ConnectionSettingsView(CreateConnectionSettings(false));
        ConnectionSettingsContent.Visibility = Visibility.Visible;
    }

    private void ReleaseConnectionSetup()
    {
        startupCancellation?.Cancel();
        if (ConnectionSetupHost.Content is Frame { Content: ConnectionSetupPage page })
            page.Release();
        // Frameの現在エントリに渡したVMも含め、ホストごと参照を解放する。
        ConnectionSetupHost.Content = null;
        ConnectionSetupHost.Visibility = Visibility.Collapsed;
    }

    private void ReleaseConnectionSettings()
    {
        (ConnectionSettingsContent.Content as ConnectionSettingsView)?.ViewModel.Dispose();
        ConnectionSettingsContent.Content = null;
    }

    private async Task ConnectAsync(ApiEndpoint candidate, CancellationToken token)
    {
        if (!EditorAwaitingLogin && !await LeaveRecordEditorAsync()) return;
        if (token.IsCancellationRequested)
            return;
        await RunAsync(async () =>
        {
            using var timeout = ConnectionTimeout();
            try
            {
                // 確認・保存までは画面寿命に従う。確定後の認証/復帰はWindowが所有する。
                using var cancellation = token.Register(timeout.Cancel);
                await connection.ConnectAsync(candidate, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            await RestoreAndOpenAsync(timeout.Token);
        }, () => ConnectAsync(candidate, lifetime.Token), true);
    }

    private bool EditorAwaitingLogin => transferAwaitingLogin || receiptRecoveryPhase == ReceiptRecoveryPhase.Login;

    // ログインへの移動準備。画面表示だけでは編集状態を破棄しない。
    private void NavigateToLogin(string? message = null)
    {
        var suspended = SuspendTransferForAuthentication() || SuspendReceiptForAuthentication();
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
        AccountNavigation.IsEnabled = LogoutNavigation.IsEnabled = false;
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
            if (account is null) NavigateToLogin("ログインを続けられませんでした。もう一度ログインしてください。");
            else await OpenDestinationAsync(cancellation.Token);
            if (!closed) BringToFront();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !closed) { NavigateToLogin(); }
        catch (AuthenticationRejectedException) { NavigateToLogin("ログインできませんでした。もう一度ログインしてください。"); }
        finally { browserLogin = null; browserCancellation = null; if (!closed) ReopenBrowserButton.IsEnabled = CancelLoginButton.IsEnabled = true; }
    }, RetryStartupAsync, true);

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

    private async Task LogoutAsync()
    {
        if (!await LeaveRecordEditorAsync()) return;
        await RunAsync(async () =>
    {
        DropPendingCategoryEdit();
        DropPendingPaymentEdit();
        DropPendingPersonEdit();
        pendingParticipantEdit = null;
        await connection.Session.LogoutAsync(lifetime.Token);
        account = null;
        DiscardLedgerContents();
        ClearLedger();
        ShowLogin();
    }, LogoutAsync);
    }

    private async Task RunAsync(Func<Task> action, Func<Task> repeat, bool connectionFailure = false)
    {
        if (busy || closed) return;
        var receiptView = RememberReceiptAuthenticationView();
        busy = true;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        SetOperationsEnabled(false);
        try { await action(); }
        catch (OperationCanceledException) when (closed) { }
        catch (ObjectDisposedException) when (closed) { }
        catch (AuthenticationRejectedException)
        {
            RememberCategoryEdit();
            RememberPaymentEdit();
            RememberPersonEdit();
            RememberParticipantEdit();
            NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (AuthenticationContextChangedException error) { pendingParticipantEdit = null; DropPendingCategoryEdit(); DropPendingPaymentEdit(); DropPendingPersonEdit(); NavigateToLogin(error.Message); }
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
        catch (CategoryRequestException error)
        {
            ApplyCategoryFailureContext(error);
            ReportFailure("科目一覧を取得できませんでした。参加状態と権限を確認してください。", repeat, connectionFailure);
        }
        catch (PaymentMethodRequestException error)
        {
            ApplyPaymentFailureContext(error);
            ReportFailure("支払方法一覧を取得できませんでした。参加状態と権限を確認してください。", repeat, connectionFailure);
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
            if (receiptRecoveryPhase == ReceiptRecoveryPhase.None && ReferenceEquals(receiptAuthentication, receiptView)) receiptAuthentication = null;
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
        CategoryDialog.IsEnabled = enabled;
        PaymentDialog.IsEnabled = enabled;
        PersonDialog.IsEnabled = ParticipantDialog.IsEnabled = InvitationCancelDialog.IsEnabled = enabled;
        UpdatePeopleActions();
        var showProgress = !enabled && !IsStartupVisible;
        BusyProgress.IsActive = showProgress;
        BusyProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPage(FrameworkElement page)
    {
        // 未承認の遷移からも編集中データを黙って捨てない。明示ナビゲーションは先に離脱確認する。
        var authenticationPage = ReferenceEquals(page, LoginPage) || ReferenceEquals(page, BrowserPage) || ReferenceEquals(page, FailurePage);
        if (RecordEditorActive && !ReferenceEquals(page, HomePage) && !(EditorAwaitingLogin && authenticationPage)) return;
        if (EditorAwaitingLogin && ReferenceEquals(page, HomePage)) return;
        ReleaseConnectionSetup();
        LegacyContent.Visibility = Visibility.Visible;
        ReleaseConnectionSettings();
        if (!ReferenceEquals(page, HomePage))
        {
            CancelReceiptOpening();
            CancelTransferOpening();
            ReceiptListModel?.Suspend();
            TransferListModel?.Suspend();
        }
        foreach (var candidate in new FrameworkElement[] { HomePage, ManagementPage, LoginPage,
            BrowserPage, FailurePage, LedgerChoicePage, LedgerCreatePage })
            candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(page, HomePage)) DispatcherQueue.TryEnqueue(async () =>
        {
            if (closed || HomePage.Visibility != Visibility.Visible) return;
            if (ReceiptListModel is null) StartReceiptHome();
            else if (!RecordEditorActive)
            {
                await Task.WhenAll(ReceiptListModel.ResumeAsync(lifetime.Token), TransferListModel?.ResumeAsync(lifetime.Token) ?? Task.CompletedTask);
                ReceiptsView.ObserveViewport();
                TransfersView.ObserveViewport();
            }
            if (transferRestorePending) { transferRestorePending = false; RestoreTransferPosition(); }
            if (receiptRestorePending) { receiptRestorePending = false; RestoreReceiptListPosition(); }
        });
    }

    private void ShowError(string message)
    {
        if (categoryDialogOpen && !categoryDialogClosing && !forceCategoryClose) { ShowCategoryError(null, message); return; }
        if (orderedCategories is not null) { ShowCategoryOrderNotice(message); return; }
        if (paymentDialogOpen && !paymentDialogClosing && !forcePaymentClose) { ShowPaymentError(null, message); return; }
        if (orderedPayments is not null) { ShowPaymentOrderNotice(message); return; }
        if (participantDialogOpen && !forceParticipantClose) { ShowParticipantError(message); return; }
        if (invitationDialogOpen && !forceParticipantClose)
        { InvitationCancelError.Text = message; InvitationCancelError.Visibility = Visibility.Visible; return; }
        if (InvitationsContent.Visibility == Visibility.Visible) { ShowInvitationNotice(message); return; }
        if (IsOrderingPeople)
        {
            ShowPersonOrderNotice(message);
            return;
        }
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
        ReleaseConnectionSetup();
        ReleaseConnectionSettings();
        transferAuthentication = null;
        transferEditor = null;
        ReceiptListModel?.Dispose();
        CancelTransferOpening();
        var transferModel = TransferEntry.ViewModel;
        TransferEntry.ViewModel = null;
        transferModel?.DiscardWrite();
        TransferEntry.CancelRequests();
        TransferListModel?.Dispose(); recordMasterLifetime?.Cancel(); recordMasterLifetime?.Dispose();
        ResetReceiptEditor();
        connection.Masters.Changed -= ReceiptMasterChanged;
        DiscardCategoryEditor();
        DiscardPaymentEditor();
        DiscardPersonEditor();
        ResetParticipants();
        lifetime.Cancel();
        browserCancellation?.Cancel();
        connection.Dispose();
    }
}
