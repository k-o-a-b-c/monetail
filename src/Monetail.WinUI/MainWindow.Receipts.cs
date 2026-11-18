using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptListViewModel? ReceiptListModel => ReceiptsView.ViewModel;
    private ReceiptSearchState? receiptSearch => recordSearch?.Receipts;
    private RecordSearchState? recordSearch;
    private CancellationTokenSource? recordMasterLifetime;
    private FormKeyboard recordKeyboard = null!;
    private bool receiptFiltersUpdating;
    internal bool ReceiptHomeReady => account is null || (ReceiptListModel is { HasLoaded: true } or { Failure: not null }) && TransferHomeReady;

    private void InitializeReceipts()
    {
        recordKeyboard = new FormKeyboard(RecordFilters, null, null, ReceiptMonth);
        connection.Masters.Changed += ReceiptMasterChanged;
    }
    private void ResetRecordHome()
    {
        receiptRestorePending = false;
        ResetTransfers(); recordSearch = null; recordMasterLifetime?.Cancel(); recordMasterLifetime?.Dispose(); recordMasterLifetime = null;
        if (ReceiptListModel is { } model)
        {
            model.PropertyChanged -= ReceiptListChanged;
            model.SearchRequested -= ApplyReceiptTextAsync;
            model.OpenRequested -= OpenReceiptEditorAsync;
            model.Dispose();
        }
        ReceiptsView.ViewModel = null;
        connection.Masters.Clear();
    }
    private void StartReceiptHome()
    {
        if (account is null || currentLedger is null) return;
        var ledger = currentLedger.LedgerId; var api = connection.Api; var session = connection.Session;
        recordSearch = new(DateOnly.FromDateTime(DateTime.Now));
        recordMasterLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var masterToken = recordMasterLifetime.Token;
        receiptFiltersUpdating = true; ReceiptMonth.Text = recordSearch.Receipts.MonthText; RecordMonthPeriod.IsChecked = true; receiptFiltersUpdating = false;
        var workspace = new ReceiptsWorkspace(api, session, ledger);
        var model = new ReceiptListViewModel(async (query, token) =>
        {
            await GetRecordMastersAsync(ledger, api, session, masterToken, token);
            return await workspace.ListAsync(query, token);
        }, recordSearch, id => connection.Masters.PaymentName(ledger, id));
        model.SearchRequested += ApplyReceiptTextAsync;
        model.OpenRequested += OpenReceiptEditorAsync;
        model.PropertyChanged += ReceiptListChanged;
        ReceiptsView.ViewModel = model;
        UpdateReceiptActions();
        RefreshReceiptChoices();
        RenderRecordFilters();
        _ = model.StartAsync(recordSearch.Receipts.Query(), lifetime.Token);
        StartTransferHome(ledger, api, session, masterToken);
    }
    private async Task GetRecordMastersAsync(Guid ledger, MonetailApiClient api, ClientSession session, CancellationToken masterToken, CancellationToken token)
    {
        // 検索変更で片方の待機を中断しても、もう片方が共有するマスタ取得は中断しない。
        token.ThrowIfCancellationRequested();
        await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), masterToken).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), masterToken).WaitAsync(token);
    }
    private void ReceiptMasterChanged(Guid ledger)
    {
        if (currentLedger?.LedgerId != ledger || closed) return;
        RefreshReceiptEditorChoices(); RefreshTransferEditorChoices(); RenderTransferEditor();
        RefreshReceiptChoices(); ReceiptListModel?.RefreshNames();
        TransferListModel?.RefreshNames(); UpdateTransferActions();
    }
    private void RefreshReceiptChoices()
    {
        if (receiptSearch is null || currentLedger is null) return;
        var ledger = currentLedger.LedgerId; var cache = connection.Masters;
        var people = cache.People(ledger)?.People ?? [];
        var mine = cache.MyPersonId(ledger);
        receiptFiltersUpdating = true;
        var owners = new[] { new ReceiptFilterChoice(null, "全員") }.Concat(people.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder)
            .Select(p => new ReceiptFilterChoice(p.PersonId, p.Name))).ToArray();
        ReceiptOwner.ItemsSource = owners; ReceiptOwner.SelectedItem = owners.FirstOrDefault(p => p.Id == receiptSearch.OwnerPersonId);
        var methods = cache.Payments(ledger)?.PaymentMethods ?? [];
        var order = people.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder).Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        var payments = new[] { new ReceiptFilterChoice(null, "全件") }.Concat(methods.OrderBy(p => order.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder)
            .Select(p => new ReceiptFilterChoice(p.PaymentMethodId, cache.PaymentName(ledger, p.PaymentMethodId)))).ToArray();
        ReceiptPayment.ItemsSource = payments; ReceiptPayment.SelectedItem = payments.FirstOrDefault(p => p.Id == receiptSearch.PaymentMethodId);
        receiptFiltersUpdating = false;
    }
    private void ReceiptListChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (closed || !ReferenceEquals(sender, ReceiptListModel) || args.PropertyName != nameof(ReceiptListViewModel.Failure))
            return;
        if (!RecordEditorActive && ReceiptListModel?.Failure is (AuthenticationRejectedException or AuthenticationContextChangedException))
            NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
    }
    private void UpdateReceiptActions()
    {
        if (ReceiptListModel is { } model)
            model.CanRegister = account is not null && receiptOpening is null;
    }
    private void RenderRecordFilters()
    {
        if (closed)
            return;
        ReceiptMonthError.Text = receiptSearch?.MonthError ?? "";
        ReceiptMonthError.Visibility = receiptSearch?.MonthError is null ? Visibility.Collapsed : Visibility.Visible;
        if (receiptSearch is { } search)
        {
            ReceiptAppliedMonth.Text = "適用中：" + (search.AllPeriods ? "全期間" : search.AppliedMonth);
            ReceiptMonth.IsEnabled = !search.AllPeriods; RecordMonthApply.IsEnabled = !search.AllPeriods; ReceiptPrevious.IsEnabled = search.CanMove(-1); ReceiptNextMonth.IsEnabled = search.CanMove(1);
        }
    }
    private async Task ApplyReceiptTextAsync()
    {
        if (receiptSearch is null || ReceiptListModel is null) return;
        var previousMonth = receiptSearch.Query().Month;
        receiptSearch.MonthText = ReceiptMonth.Text;
        if (!receiptSearch.ApplyText()) { RenderRecordFilters(); ReceiptMonth.Focus(FocusState.Programmatic); return; }
        await ReloadRecordsAsync(true, receiptSearch.Query().Month != previousMonth);
    }
    private async void ReceiptTextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || recordKeyboard.IsComposing ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)) return;
        args.Handled = true;
        await ApplyRecordMonthAsync();
    }
    private void ReceiptDraftChanged(object sender, TextChangedEventArgs args)
    { if (!receiptFiltersUpdating && receiptSearch is { } search) { search.MonthText = ReceiptMonth.Text; } }
    private async void MoveReceiptMonth(object sender, RoutedEventArgs args)
    {
        if (receiptSearch is null || ReceiptListModel is null || !receiptSearch.MoveMonth(ReferenceEquals(sender, ReceiptPrevious) ? -1 : 1)) return;
        receiptFiltersUpdating = true; ReceiptMonth.Text = receiptSearch.MonthText; receiptFiltersUpdating = false;
        await ReloadRecordsAsync(true, true);
    }
    private async void ReceiptPeriodChanged(object sender, RoutedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || ReceiptListModel is null) return;
        receiptSearch.AllPeriods = ReferenceEquals(sender, ReceiptAllPeriods); await ReloadRecordsAsync(true, true);
    }
    private async void ReceiptOwnerChanged(object sender, SelectionChangedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || ReceiptListModel is null || ReceiptOwner.SelectedItem is not ReceiptFilterChoice value) return;
        receiptSearch.OwnerPersonId = value.Id; await ReloadRecordsAsync(true, true);
    }
    private async void ReceiptPaymentChanged(object sender, SelectionChangedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || ReceiptListModel is null || ReceiptPayment.SelectedItem is not ReceiptFilterChoice value) return;
        receiptSearch.PaymentMethodId = value.Id; await ReloadRecordsAsync(true, true);
    }
    private async void ApplyRecordMonth(object sender, RoutedEventArgs args) => await ApplyRecordMonthAsync();
    private async Task ApplyRecordMonthAsync()
    {
        if (receiptSearch is null) return;
        receiptSearch.MonthText = ReceiptMonth.Text;
        if (!receiptSearch.ApplyMonthText()) { RenderRecordFilters(); ReceiptMonth.Focus(FocusState.Programmatic); return; }
        await ReloadRecordsAsync(true, true);
    }
    private async Task ReloadRecordsAsync(bool receipts, bool transfers)
    {
        if (receiptSearch is null || recordSearch is null) return;
        var receipt = receipts && ReceiptListModel is not null ? ReceiptListModel.StartAsync(receiptSearch.Query(), lifetime.Token) : Task.CompletedTask;
        var transfer = transfers && TransferListModel is not null ? TransferListModel.StartAsync(recordSearch.TransferQuery(), lifetime.Token) : Task.CompletedTask;
        RenderRecordFilters(); await Task.WhenAll(receipt, transfer);
    }
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) return match; if (Descendant<T>(child) is { } found) return found; }
        return null;
    }
}
