using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.WinUI.Views.Ledgers;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private LedgerInfo? currentLedger;
    private IReadOnlyList<LedgerInfo> ledgerChoices = [];
    private LedgerCreationDraft? creationDraft;
    private MenuFlyout? ledgerMenu;
    private ContentDialog? ledgerSwitchDialog;

    private async Task OpenDestinationAsync(CancellationToken token)
    {
        if (await ResumeTransferEditAsync(token)) return;
        if (await ResumeReceiptEditAsync(token)) return;
        if (await ResumeManagementAsync(token)) return;
        await OpenInitialLedgerAsync(token);
    }

    // 最後の家計簿・所属一覧・表示先の判断は既存Workspaceに任せ、ここでは表示だけを決める。
    private async Task OpenInitialLedgerAsync(CancellationToken token)
    {
        var page = CurrentPage;
        var destination = await connection.Ledgers.StartAsync(token);
        token.ThrowIfCancellationRequested();
        if (closed || !ReferenceEquals(CurrentPage, page))
            return;
        ledgerChoices = destination.Choices;
        if (destination.Opened is not null) DisplayLedger(destination.Opened);
        else if (ledgerChoices.Count == 0) ShowCreation();
        else
        {
            if (WorkspaceHost.Content is Monetail.WinUI.Views.LedgerChoiceView choices) choices.Ledgers = ledgerChoices;
            NavigateWorkspace(WorkspaceScreen.LedgerChoicePage);
        }
    }

    // 家計簿を離れることが確定した時だけ呼ぶ。401退避では振替の入力を保持する。
    private void DiscardLedgerContents()
    {
        if (transferAuthentication is not null) DiscardTransferAuthentication();
        else ResetTransferEditor();
        if (receiptAuthentication is not null) DiscardReceiptAuthentication();
        else ResetReceiptEditor();
        ResetRecordHome();
        DiscardManagement();
    }

    // 選択中の家計簿とヘッダーだけを解除する。編集状態の破棄は呼出元で明示する。
    private void ClearLedger()
    {
        currentLedger = null;
        ledgerChoices = [];
        creationDraft = null;
        if (WorkspaceHost.Content is Monetail.WinUI.Views.LedgerChoiceView choices) choices.Ledgers = ledgerChoices;
        ledgerMenu = null;
        LedgerSelector.IsEnabled = false;
        LedgerName.Text = "家計簿を選択";
    }

    private LedgerCreatePage? CreationView => (LedgerPageHost.Content as Frame)?.Content as LedgerCreatePage;

    private void ShowCreation()
    {
        // 所属0件の再確認では、入力と同名の作成再送要求をそのまま保持する。
        if (CreationView is not null)
            return;
        creationDraft = null;
        ReleaseAuthentication();
        HideWorkspace();
        AuthenticationNotice(null);
        var frame = new Frame { IsNavigationStackEnabled = false };
        LedgerPageHost.Content = frame;
        LedgerPageHost.Visibility = Visibility.Visible;
        frame.Navigate(typeof(LedgerCreatePage), currentLedger is null && ledgerChoices.Count == 0);
        if (CreationView is { } creation)
        {
            creation.IsEnabled = !busy;
            creation.CreateRequested += CreateLedger;
            creation.RefreshRequested += RefreshLedgerMembership;
            creation.LogoutRequested += LogoutFromCreation;
            creation.CancelRequested += CancelCreation;
        }
    }

    private void ReleaseLedgerPage()
    {
        if (CreationView is { } creation)
        {
            creation.CreateRequested -= CreateLedger;
            creation.RefreshRequested -= RefreshLedgerMembership;
            creation.LogoutRequested -= LogoutFromCreation;
            creation.CancelRequested -= CancelCreation;
            creationDraft = null;
        }
        LedgerPageHost.Content = null;
        LedgerPageHost.Visibility = Visibility.Collapsed;
    }

    private async void CreateLedger(object? sender, string name)
    {
        if (!busy && !closed && ReferenceEquals(sender, CreationView))
            await CreateLedgerAsync(name);
    }

    private Task CreateLedgerAsync(string input)
    {
        try
        {
            var name = input.Trim();
            if (creationDraft?.Name != name)
                creationDraft = new LedgerCreationDraft(name);
        }
        catch (FormatException error)
        {
            ErrorNotice.IsOpen = false;
            ErrorNotice.Visibility = Visibility.Collapsed;
            CreationView?.ShowNameError(error.Message);
            return Task.CompletedTask;
        }
        var draft = creationDraft;
        return RunAsync(async () =>
        {
            var opened = await connection.Ledgers.CreateAsync(draft!, lifetime.Token);
            creationDraft = null;
            DisplayLedger(opened);
        });
    }

    private async void RefreshLedgerMembership(object sender, RoutedEventArgs args)
    {
        if (busy || closed || sender is not LedgerCreatePage page || !ReferenceEquals(page, CreationView))
            return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        RoutedEventHandler leave = (_, _) => cancellation.Cancel();
        page.Unloaded += leave;
        try
        {
            await RunAsync(async () =>
            {
                try
                {
                    await OpenInitialLedgerAsync(cancellation.Token);
                    if (ReferenceEquals(CreationView, page))
                        page.ShowEmptyMembership();
                }
                catch (Exception error) when ((cancellation.IsCancellationRequested || !ReferenceEquals(CreationView, page)) &&
                    error is OperationCanceledException or ApiConnectionException or HttpRequestException or
                        AuthenticationRejectedException or AuthenticationContextChangedException or LedgerRequestException or
                        IOException or UnauthorizedAccessException or CryptographicException or SocketException)
                {
                    // 離脱後の遅延応答/失敗は新画面へ出さない。表示中の失敗分類は共通RunAsyncへ委ねる。
                }
            });
        }
        finally
        {
            page.Unloaded -= leave;
        }
    }

    private async void LogoutFromCreation(object sender, RoutedEventArgs args)
    {
        if (!busy && !closed && ReferenceEquals(sender, CreationView))
            await LogoutAsync();
    }

    private void CancelCreation(object sender, RoutedEventArgs args)
    {
        if (busy || closed || !ReferenceEquals(sender, CreationView))
            return;
        NavigateWorkspace(currentLedger is null ? WorkspaceScreen.LedgerChoicePage : WorkspaceScreen.HomePage);
    }

    private async void OpenSelectedLedger(object? sender, Guid ledgerId) => await OpenLedgerAsync(ledgerId);

    private async Task OpenLedgerAsync(Guid ledgerId)
    {
        if (closed || busy || ManagementDialogOpen)
            return;
        ledgerMenu?.Hide();
        if (ManagementPage is not null && currentLedger?.LedgerId == ledgerId)
            return;
        if (!await LeaveRecordEditorAsync() || !await ConfirmManagementLedgerLeaveAsync())
            return;
        await RunAsync(async () =>
    {
        var opened = await connection.Ledgers.OpenAsync(ledgerId, lifetime.Token);
        DisplayLedger(opened);
    });
    }

    private async Task<bool> ConfirmManagementLedgerLeaveAsync()
    {
        if (closed || busy || ManagementDialogOpen)
            return false;
        if (ManagementPage is not { } page || !page.ViewModel.HasOrderChanges)
            return true;
        var ledgerId = currentLedger?.LedgerId;
        var accountId = account?.AuthUserId;
        var dialog = new ContentDialog
        {
            XamlRoot = WorkspaceRoot.XamlRoot,
            Title = "未保存の並び順があります",
            Content = "現在の管理画面を離れると、未保存の並び順は破棄されます。",
            PrimaryButtonText = "破棄して切替",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close
        };
        ledgerSwitchDialog = dialog;
        try
        {
            using var cancellation = lifetime.Token.Register(dialog.Hide);
            return await dialog.ShowAsync() == ContentDialogResult.Primary && !closed &&
                ReferenceEquals(page, CurrentPage) && currentLedger?.LedgerId == ledgerId &&
                account?.AuthUserId == accountId;
        }
        finally
        {
            ledgerSwitchDialog = null;
        }
    }

    private async void ShowLedgerMenu(object sender, RoutedEventArgs args)
    {
        if (closed || busy || ManagementDialogOpen || !await LeaveRecordEditorAsync())
            return;
        if (closed)
            return;
        ledgerMenu?.Hide();
        ledgerMenu = null;
        var page = CurrentPage;
        await RunAsync(async () =>
        {
            var choices = await connection.Ledgers.ListAsync(lifetime.Token);
            var menu = new MenuFlyout();
            foreach (var ledger in choices)
            {
                var item = new MenuFlyoutItem { Text = ledger.Name };
                item.Click += async (_, _) =>
                {
                    if (ReferenceEquals(page, CurrentPage))
                        await OpenLedgerAsync(ledger.LedgerId);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "＋追加" };
            create.Click += async (_, _) =>
            {
                if (closed || busy || ManagementDialogOpen || !ReferenceEquals(page, CurrentPage))
                    return;
                menu.Hide();
                if (await ConfirmManagementLedgerLeaveAsync())
                    ShowCreation();
            };
            menu.Items.Add(create);
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(ledgerMenu, menu))
                    ledgerMenu = null;
            };
            if (ReferenceEquals(page, CurrentPage))
                ledgerMenu = menu;
        });
        if (!closed && !ManagementDialogOpen && ReferenceEquals(page, CurrentPage) &&
            WorkspaceHost.Visibility == Visibility.Visible && WorkspaceHeader.Visibility == Visibility.Visible)
            ledgerMenu?.ShowAt(LedgerSelector);
    }

}
