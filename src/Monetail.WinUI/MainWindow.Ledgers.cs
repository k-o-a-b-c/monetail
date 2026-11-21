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

    private async Task OpenDestinationAsync(CancellationToken token)
    {
        if (await ResumeTransferEditAsync(token)) return;
        if (await ResumeReceiptEditAsync(token)) return;
        if (await ResumeCategoryEditAsync(token)) return;
        if (await ResumePaymentEditAsync(token)) return;
        if (await ResumeParticipantEditAsync(token)) return;
        if (await ResumePersonEditAsync(token)) return;
        await OpenInitialLedgerAsync(token);
    }

    // 最後の家計簿・所属一覧・表示先の判断は既存Workspaceに任せ、ここでは表示だけを決める。
    private async Task OpenInitialLedgerAsync(CancellationToken token)
    {
        var frame = AuthenticationHost.Content;
        var destination = await connection.Ledgers.StartAsync(token);
        token.ThrowIfCancellationRequested();
        if (closed || !ReferenceEquals(AuthenticationHost.Content, frame))
            return;
        ledgerChoices = destination.Choices;
        if (destination.Opened is not null) DisplayLedger(destination.Opened);
        else if (ledgerChoices.Count == 0) ShowCreation();
        else
        {
            LedgerChoicePage.Ledgers = ledgerChoices;
            ShowPage(LedgerChoicePage);
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
        ResetCategories();
        ResetPayments();
        ResetPeople();
        ResetParticipants();
    }

    // 選択中の家計簿とヘッダーだけを解除する。編集状態の破棄は呼出元で明示する。
    private void ClearLedger()
    {
        currentLedger = null;
        ledgerChoices = [];
        creationDraft = null;
        LedgerChoicePage.Ledgers = ledgerChoices;
        PeopleList.ItemsSource = null;
        ledgerMenu = null;
        LedgerSelector.IsEnabled = false;
        LedgerName.Text = "家計簿を選択";
        LedgerContext.Text = "家計簿が開かれていません";
    }

    private LedgerCreatePage? CreationView => (AuthenticationHost.Content as Frame)?.Content as LedgerCreatePage;

    private void ShowCreation()
    {
        // 所属0件の再確認では、入力と同名の作成再送要求をそのまま保持する。
        if (CreationView is not null)
            return;
        creationDraft = null;
        OpenAuthenticationPage(typeof(LedgerCreatePage), currentLedger is null && ledgerChoices.Count == 0);
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
        ShowPage(currentLedger is null ? LedgerChoicePage : HomePage);
    }

    private async void OpenSelectedLedger(object? sender, Guid ledgerId) => await OpenLedgerAsync(ledgerId);

    private async Task OpenLedgerAsync(Guid ledgerId)
    {
        if (!await LeaveRecordEditorAsync()) return;
        await RunAsync(async () =>
    {
        var opened = await connection.Ledgers.OpenAsync(ledgerId, lifetime.Token);
        PeopleList.ItemsSource = null;
        EmptyPeople.Text = "人物一覧を読み込んでいます。";
        EmptyPeople.Visibility = Visibility.Visible;
        DisplayLedger(opened);
    });
    }

    private async void ShowLedgerMenu(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !await LeaveRecordEditorAsync()) return;
        ledgerMenu = null;
        await RunAsync(async () =>
        {
            var choices = await connection.Ledgers.ListAsync(lifetime.Token);
            var menu = new MenuFlyout();
            foreach (var ledger in choices)
            {
                var item = new MenuFlyoutItem { Text = ledger.Name };
                item.Click += async (_, _) => await OpenLedgerAsync(ledger.LedgerId);
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "＋追加" };
            create.Click += (_, _) => ShowCreation();
            menu.Items.Add(create);
            ledgerMenu = menu;
        });
        if (!closed && HomePage.Visibility == Visibility.Visible) ledgerMenu?.ShowAt(LedgerSelector);
    }

    private Task LoadPeopleAsync()
    {
        if (account is null || currentLedger is null || !CanViewPeople) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var workspace = new PeopleWorkspace(connection.Api, connection.Session, currentLedger.LedgerId);
            DisplayPeople(await workspace.ListAsync(lifetime.Token));
        });
    }

    private async void SelectManagement(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || busy || ManagementDialogOpen) return;
        ReleaseConnectionSettings();
        DropPendingCategoryEdit(); DiscardCategoryEditor(); DiscardCategoryOrder();
        CategoriesContent.Visibility = Visibility.Collapsed;
        DropPendingPaymentEdit(); DiscardPaymentEditor(); DiscardPaymentOrder();
        PaymentsContent.Visibility = Visibility.Collapsed;
        DropPendingPersonEdit();
        DiscardPersonEditor();
        DiscardPersonOrder();
        PeopleContent.Visibility = ParticipantsContent.Visibility = InvitationsContent.Visibility = ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(item, ParticipantsNavigation))
        {
            if (!CanManageParticipants) return;
            ParticipantsContent.Visibility = Visibility.Visible;
            await LoadParticipantsAsync();
        }
        else if (ReferenceEquals(item, CategoriesNavigation))
        {
            CategoriesContent.Visibility = Visibility.Visible;
            await LoadCategoriesAsync();
        }
        else if (ReferenceEquals(item, PaymentsNavigation))
        {
            PaymentsContent.Visibility = Visibility.Visible;
            await LoadPaymentsAsync();
        }
        else if (ReferenceEquals(item, PeopleNavigation))
        {
            PeopleContent.Visibility = Visibility.Visible;
            await LoadPeopleAsync();
        }
        else if (ReferenceEquals(item, ConnectionNavigation))
        {
            OpenConnectionSettings();
        }
        else if (ReferenceEquals(item, AccountNavigation))
        {
            AccountName.Text = account?.DisplayName ?? "";
            AccountServer.Text = connection.Endpoint?.Value ?? "";
            AccountContent.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(item, LogoutNavigation))
        {
            // 失敗時も本人と開いている家計簿を保持する。
            ManagementNavigation.SelectedItem = AccountNavigation;
            AccountContent.Visibility = Visibility.Visible;
            AccountName.Text = account?.DisplayName ?? "";
            AccountServer.Text = connection.Endpoint?.Value ?? "";
            await LogoutAsync();
        }
    }


}
