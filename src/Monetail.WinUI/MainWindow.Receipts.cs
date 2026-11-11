using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptListState? receiptState;
    private ReceiptSearchState? receiptSearch;
    private readonly ObservableCollection<ReceiptRow> receiptRows = [];
    private ScrollViewer? receiptScroller;
    private FormKeyboard receiptKeyboard = null!;
    private bool receiptFiltersUpdating, receiptViewportQueued, receiptScrollIntent;
    private double receiptPreviousOffset;
    internal bool ReceiptHomeReady => account is null || receiptState is { HasLoaded: true } or { Failure: not null };

    private void InitializeReceipts()
    {
        ReceiptList.ItemsSource = receiptRows;
        receiptKeyboard = new FormKeyboard(ReceiptFilters, null, null, ReceiptMonth, ReceiptShop);
        connection.Masters.Changed += ReceiptMasterChanged;
        ReceiptList.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => receiptScrollIntent = receiptScroller?.ScrollableHeight > 0), true);
        ReceiptList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is ScrollBar) { receiptScrollIntent = true; break; }
        }), true);
        ReceiptList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        { if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End or VirtualKey.Space) receiptScrollIntent = receiptScroller?.ScrollableHeight > 0; }), true);
    }
    private void ResetReceipts()
    {
        receiptState?.Dispose(); receiptState = null; receiptSearch = null;
        connection.Masters.Clear(); receiptRows.Clear(); receiptScrollIntent = false;
    }
    private void StartReceiptHome()
    {
        if (account is null || currentLedger is null) return;
        var ledger = currentLedger.LedgerId; var api = connection.Api; var session = connection.Session;
        receiptSearch = new(DateOnly.FromDateTime(DateTime.Now));
        receiptFiltersUpdating = true; ReceiptMonth.Text = receiptSearch.MonthText; ReceiptShop.Text = ""; ReceiptAllPeriods.IsChecked = false; receiptFiltersUpdating = false;
        var workspace = new ReceiptsWorkspace(api, session, ledger);
        var state = new ReceiptListState(async (query, token) =>
        {
            await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), token);
            await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), token);
            return await workspace.ListAsync(query, token);
        });
        receiptState = state;
        state.Rows.CollectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(receiptState, state)) return;
            if (args.Action == NotifyCollectionChangedAction.Reset) { receiptScrollIntent = receiptQueuedUserScroll = false; receiptPreviousOffset = 0; receiptRows.Clear(); receiptScroller?.ChangeView(null, 0, null, true); }
            else if (args.NewItems is not null)
                foreach (ReceiptSummaryInfo row in args.NewItems) receiptRows.Add(new(row, id => connection.Masters.PaymentName(ledger, id)));
        };
        state.Changed += () => { if (ReferenceEquals(receiptState, state)) RenderReceiptState(); };
        RefreshReceiptChoices(); _ = state.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private void ReceiptMasterChanged(Guid ledger)
    {
        if (currentLedger?.LedgerId != ledger || closed) return;
        RefreshReceiptChoices(); foreach (var row in receiptRows) row.RefreshName();
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
    private void RenderReceiptState()
    {
        if (closed || receiptState is not { } state) return;
        if (state.Failure is AuthenticationRejectedException or AuthenticationContextChangedException)
        { ShowLogin("ログインを継続できません。もう一度ログインしてください。"); return; }
        ReceiptInitialProgress.IsActive = state.IsInitialLoading;
        ReceiptInitialNotice.Visibility = state.IsInitialLoading ? Visibility.Visible : Visibility.Collapsed;
        ReceiptEmpty.Visibility = state.HasLoaded && state.Rows.Count == 0 && state.Failure is null && !state.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ReceiptInitialFailure.Visibility = state.IsInitialFailure ? Visibility.Visible : Visibility.Collapsed;
        ReceiptNextProgress.IsActive = state.IsContinuationLoading;
        ReceiptNextNotice.Visibility = state.IsContinuationLoading ? Visibility.Visible : Visibility.Collapsed;
        ReceiptNextFailure.Visibility = state.IsContinuationFailure ? Visibility.Visible : Visibility.Collapsed;
        ReceiptContinue.Visibility = state.CanContinue ? Visibility.Visible : Visibility.Collapsed;
        ReceiptInitialError.Text = state.Failure switch
        {
            ReceiptRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
            ReceiptRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
            ReceiptRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
            PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
            _ => "レシートを取得できませんでした"
        };
        ReceiptMonthError.Text = receiptSearch?.MonthError ?? "";
        ReceiptMonthError.Visibility = receiptSearch?.MonthError is null ? Visibility.Collapsed : Visibility.Visible;
        if (receiptSearch is { } search)
        {
            ReceiptAppliedMonth.Text = "適用中：" + (search.AllPeriods ? "全期間" : search.AppliedMonth);
            ReceiptMonth.IsEnabled = !search.AllPeriods; ReceiptPrevious.IsEnabled = search.CanMove(-1); ReceiptNextMonth.IsEnabled = search.CanMove(1);
        }
        ScheduleReceiptViewport(false);
    }
    private async void ApplyReceiptText(object sender, RoutedEventArgs args) => await ApplyReceiptTextAsync();
    private async Task ApplyReceiptTextAsync()
    {
        if (receiptSearch is null || receiptState is null) return;
        receiptSearch.MonthText = ReceiptMonth.Text; receiptSearch.ShopText = ReceiptShop.Text;
        if (!receiptSearch.ApplyText()) { RenderReceiptState(); ReceiptMonth.Focus(FocusState.Programmatic); return; }
        await receiptState.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private async void ReceiptTextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || receiptKeyboard.IsComposing ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)) return;
        args.Handled = true; await ApplyReceiptTextAsync();
    }
    private void ReceiptDraftChanged(object sender, TextChangedEventArgs args)
    { if (!receiptFiltersUpdating && receiptSearch is { } search) { search.MonthText = ReceiptMonth.Text; search.ShopText = ReceiptShop.Text; } }
    private async void MoveReceiptMonth(object sender, RoutedEventArgs args)
    {
        if (receiptSearch is null || receiptState is null || !receiptSearch.MoveMonth(ReferenceEquals(sender, ReceiptPrevious) ? -1 : 1)) return;
        receiptFiltersUpdating = true; ReceiptMonth.Text = receiptSearch.MonthText; receiptFiltersUpdating = false;
        await receiptState.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private async void ReceiptPeriodChanged(object sender, RoutedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || receiptState is null) return;
        receiptSearch.AllPeriods = ReceiptAllPeriods.IsChecked == true; await receiptState.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private async void ReceiptOwnerChanged(object sender, SelectionChangedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || receiptState is null || ReceiptOwner.SelectedItem is not ReceiptFilterChoice value) return;
        receiptSearch.OwnerPersonId = value.Id; await receiptState.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private async void ReceiptPaymentChanged(object sender, SelectionChangedEventArgs args)
    {
        if (receiptFiltersUpdating || receiptSearch is null || receiptState is null || ReceiptPayment.SelectedItem is not ReceiptFilterChoice value) return;
        receiptSearch.PaymentMethodId = value.Id; await receiptState.StartAsync(receiptSearch.Query(), lifetime.Token);
    }
    private async void RetryReceipt(object sender, RoutedEventArgs args) { if (receiptState is { } state) await state.RetryAsync(lifetime.Token); }
    private async void ContinueReceipt(object sender, RoutedEventArgs args) { if (receiptState is { } state) await state.ContinueAsync(lifetime.Token); }
    private void ReceiptSelected(object sender, SelectionChangedEventArgs args) { if (receiptState is { } state) state.SelectedReceiptId = (ReceiptList.SelectedItem as ReceiptRow)?.Receipt.ReceiptId; }
    private void ReceiptListLoaded(object sender, RoutedEventArgs args)
    {
        receiptScroller = Descendant<ScrollViewer>(ReceiptList);
        if (receiptScroller is not null)
        {
            receiptScroller.ViewChanged -= ReceiptViewChanged; receiptScroller.ViewChanged += ReceiptViewChanged;
            receiptScroller.DirectManipulationStarted -= ReceiptManipulationStarted;
            receiptScroller.DirectManipulationStarted += ReceiptManipulationStarted;
        }
        ScheduleReceiptViewport(false);
    }
    private void ReceiptManipulationStarted(object? sender, object args) => receiptScrollIntent = receiptScroller?.ScrollableHeight > 0;
    private void ReceiptViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (receiptScroller is null) return;
        var user = receiptScrollIntent && receiptScroller.VerticalOffset > receiptPreviousOffset;
        receiptPreviousOffset = receiptScroller.VerticalOffset;
        if (!args.IsIntermediate) receiptScrollIntent = false;
        ScheduleReceiptViewport(user);
    }
    private bool receiptQueuedUserScroll;
    private void ScheduleReceiptViewport(bool user)
    {
        receiptQueuedUserScroll |= user;
        if (receiptViewportQueued || closed || HomePage.Visibility != Visibility.Visible || receiptState is null) return;
        receiptViewportQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            receiptViewportQueued = false; var scrolled = receiptQueuedUserScroll; receiptQueuedUserScroll = false;
            if (closed || HomePage.Visibility != Visibility.Visible || receiptState is not { } state || receiptScroller is null) return;
            var panel = ReceiptList.ItemsPanelRoot as ItemsStackPanel;
            await state.ObserveViewportAsync(panel?.LastVisibleIndex ?? -1, receiptScroller.ScrollableHeight > 0, scrolled, lifetime.Token);
        });
    }
    private void ReceiptLayoutChanged(object sender, object args) => ScheduleReceiptViewport(false);
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) return match; if (Descendant<T>(child) is { } found) return found; }
        return null;
    }
}
