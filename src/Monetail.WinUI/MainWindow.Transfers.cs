using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private TransferListState? transferState;
    private readonly ObservableCollection<TransferRow> transferRows = [];
    private ScrollViewer? transferScroller;
    private bool transferViewportQueued, transferScrollIntent;
    private double transferPreviousOffset;
    internal bool TransferHomeReady => account is null || transferState is { HasLoaded: true } or { Failure: not null };
    private void InitializeTransfers()
    {
        TransferList.ItemsSource = transferRows;
        TransferList.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => transferScrollIntent = transferScroller?.ScrollableHeight > 0), true);
        TransferList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is ScrollBar) { transferScrollIntent = true; break; }
        }), true);
        TransferList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        { if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End or VirtualKey.Space) transferScrollIntent = transferScroller?.ScrollableHeight > 0; }), true);
    }
    private void ResetTransfers()
    {
        transferState?.Dispose(); transferState = null; transferRows.Clear(); transferScrollIntent = transferQueuedUserScroll = false;
    }
    private void StartTransferHome(Guid ledger, MonetailApiClient api, ClientSession session, CancellationToken masterToken)
    {
        var workspace = new TransfersWorkspace(api, session, ledger);
        var state = new TransferListState(async (query, token) =>
        {
            await GetRecordMastersAsync(ledger, api, session, masterToken, token);
            return await workspace.ListAsync(query, token);
        });
        transferState = state;
        state.Rows.CollectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(transferState, state)) return;
            if (args.Action == NotifyCollectionChangedAction.Reset) { transferScrollIntent = transferQueuedUserScroll = false; transferPreviousOffset = 0; transferRows.Clear(); transferScroller?.ChangeView(null, 0, null, true); }
            else if (args.NewItems is not null)
                foreach (TransferSummaryInfo row in args.NewItems) transferRows.Add(new(row, id => connection.Masters.PaymentName(ledger, id)));
        };
        state.Changed += () => { if (ReferenceEquals(transferState, state)) RenderTransferState(); };
        _ = state.StartAsync(recordSearch!.TransferQuery(), lifetime.Token);
    }
    private void RenderTransferState()
    {
        if (closed || transferState is not { } state) return;
        if (receiptEditor is null && HomePage.Visibility == Visibility.Visible && state.Failure is (AuthenticationRejectedException or AuthenticationContextChangedException))
        { ShowLogin("ログインを継続できません。もう一度ログインしてください。"); return; }
        TransferInitialProgress.IsActive = state.IsInitialLoading;
        TransferInitialNotice.Visibility = state.IsInitialLoading ? Visibility.Visible : Visibility.Collapsed;
        TransferEmpty.Visibility = state.HasLoaded && state.Rows.Count == 0 && state.Failure is null && !state.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        TransferInitialFailure.Visibility = state.IsInitialFailure ? Visibility.Visible : Visibility.Collapsed;
        TransferNextProgress.IsActive = state.IsContinuationLoading;
        TransferNextNotice.Visibility = state.IsContinuationLoading ? Visibility.Visible : Visibility.Collapsed;
        TransferNextFailure.Visibility = state.IsContinuationFailure ? Visibility.Visible : Visibility.Collapsed;
        TransferContinue.Visibility = state.CanContinue ? Visibility.Visible : Visibility.Collapsed;
        TransferInitialError.Text = state.Failure switch
        {
            TransferRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
            TransferRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
            TransferRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
            PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
            _ => "振替を取得できませんでした"
        };
        ScheduleTransferViewport(false);
    }
    private void TransferDraftChanged(object sender, TextChangedEventArgs args)
    { if (!receiptFiltersUpdating && recordSearch is not null) recordSearch.TransferText = TransferNameSearch.Text; }
    private async void ApplyTransferText(object sender, RoutedEventArgs args) => await ApplyTransferTextAsync();
    private async Task ApplyTransferTextAsync()
    {
        if (recordSearch is null || receiptSearch is null) return;
        var previous = receiptSearch.Query().Month;
        receiptSearch.MonthText = ReceiptMonth.Text; recordSearch.TransferText = TransferNameSearch.Text;
        if (!recordSearch.ApplyTransferText()) { RenderReceiptState(); ReceiptMonth.Focus(FocusState.Programmatic); return; }
        await ReloadRecordsAsync(previous != receiptSearch.Query().Month, true);
    }
    private async void TransferTextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || receiptKeyboard.IsComposing ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)) return;
        args.Handled = true; await ApplyTransferTextAsync();
    }
    private async void RetryTransfer(object sender, RoutedEventArgs args) { if (transferState is { } state) await state.RetryAsync(lifetime.Token); }
    private async void ContinueTransfer(object sender, RoutedEventArgs args) { if (transferState is { } state) await state.ContinueAsync(lifetime.Token); }
    private void TransferSelected(object sender, SelectionChangedEventArgs args) { if (transferState is { } state) state.SelectedTransferId = (TransferList.SelectedItem as TransferRow)?.Transfer.TransferId; }
    private void TransferListLoaded(object sender, RoutedEventArgs args)
    {
        transferScroller = Descendant<ScrollViewer>(TransferList);
        if (transferScroller is not null)
        {
            transferScroller.ViewChanged -= TransferViewChanged; transferScroller.ViewChanged += TransferViewChanged;
            transferScroller.DirectManipulationStarted -= TransferManipulationStarted;
            transferScroller.DirectManipulationStarted += TransferManipulationStarted;
        }
        ScheduleTransferViewport(false);
    }
    private void TransferManipulationStarted(object? sender, object args) => transferScrollIntent = transferScroller?.ScrollableHeight > 0;
    private void TransferViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (transferScroller is null) return;
        var user = transferScrollIntent && transferScroller.VerticalOffset > transferPreviousOffset;
        transferPreviousOffset = transferScroller.VerticalOffset;
        if (!args.IsIntermediate) transferScrollIntent = false;
        ScheduleTransferViewport(user);
    }
    private bool transferQueuedUserScroll;
    private void ScheduleTransferViewport(bool user)
    {
        transferQueuedUserScroll |= user;
        if (transferViewportQueued || closed || HomePage.Visibility != Visibility.Visible || receiptEditor is not null || transferState is null) return;
        transferViewportQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            transferViewportQueued = false; var scrolled = transferQueuedUserScroll; transferQueuedUserScroll = false;
            if (closed || HomePage.Visibility != Visibility.Visible || receiptEditor is not null || transferState is not { } state || transferScroller is null) return;
            var panel = TransferList.ItemsPanelRoot as ItemsStackPanel;
            await state.ObserveViewportAsync(panel?.LastVisibleIndex ?? -1, transferScroller.ScrollableHeight > 0, scrolled, lifetime.Token);
        });
    }
    private void TransferLayoutChanged(object sender, object args) => ScheduleTransferViewport(false);
}
