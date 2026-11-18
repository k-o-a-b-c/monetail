using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Presentation.Transfers;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Monetail.WinUI.Views.Transfers;

public sealed partial class TransferListView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TransferListViewModel), typeof(TransferListView),
        new PropertyMetadata(null, (owner, args) => ((TransferListView)owner).ChangeModel(
            args.OldValue as TransferListViewModel, args.NewValue as TransferListViewModel)));
    public TransferListViewModel? ViewModel
    {
        get => (TransferListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public static readonly DependencyProperty OpeningContentProperty = DependencyProperty.Register(
        nameof(OpeningContent), typeof(UIElement), typeof(TransferListView), new PropertyMetadata(null));
    public UIElement? OpeningContent
    {
        get => (UIElement?)GetValue(OpeningContentProperty);
        set => SetValue(OpeningContentProperty, value);
    }
    private readonly FormKeyboard keyboard;
    private ScrollViewer? scroller;
    private bool viewportQueued, scrollIntent, queuedUserScroll;
    private double previousOffset;
    private (double Horizontal, double Vertical) returnPosition;

    public TransferListView()
    {
        InitializeComponent();
        keyboard = new FormKeyboard(this, null, null, TransferNameSearch);
        TransferList.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => scrollIntent = scroller?.ScrollableHeight > 0), true);
        TransferList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is ScrollBar)
                {
                    scrollIntent = true;
                    break;
                }
        }), true);
        TransferList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End or VirtualKey.Space)
                scrollIntent = scroller?.ScrollableHeight > 0;
        }), true);
    }
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void ChangeModel(TransferListViewModel? previous, TransferListViewModel? current)
    {
        if (previous is not null)
        {
            previous.PropertyChanged -= ModelChanged;
            ((INotifyCollectionChanged)previous.Rows).CollectionChanged -= RowsChanged;
        }
        returnPosition = default;
        ResetViewport();
        if (current is not null)
        {
            current.PropertyChanged += ModelChanged;
            ((INotifyCollectionChanged)current.Rows).CollectionChanged += RowsChanged;
        }
        Bindings.Update();
        ObserveViewport();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TransferListViewModel.SelectedItem) ||
            ViewModel is not { IsFindingSaved: true, SelectedItem: { } selected } model)
            return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(ViewModel, model) || !ReferenceEquals(model.SelectedItem, selected))
                return;
            scrollIntent = queuedUserScroll = false;
            TransferList.ScrollIntoView(selected);
        });
    }
    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
            ResetViewport();
        ObserveViewport();
    }
    private void ResetViewport()
    {
        scrollIntent = queuedUserScroll = false;
        previousOffset = 0;
        scroller?.ChangeView(null, 0, null, true);
    }
    public void RememberPosition() => returnPosition = (scroller?.HorizontalOffset ?? 0, scroller?.VerticalOffset ?? 0);
    public void RestorePosition() => DispatcherQueue.TryEnqueue(() =>
    {
        if (CanObserve())
        {
            scrollIntent = queuedUserScroll = false;
            scroller?.ChangeView(returnPosition.Horizontal, returnPosition.Vertical, null, true);
        }
    });
    private bool CanObserve()
    {
        if (!IsLoaded || scroller is null || ViewModel is null)
            return false;
        for (DependencyObject? element = this; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { Visibility: Visibility.Collapsed })
                return false;
        return true;
    }
    public void ObserveViewport() => ScheduleViewport(false);
    private void ScheduleViewport(bool user)
    {
        queuedUserScroll |= user;
        if (viewportQueued || !CanObserve())
            return;
        viewportQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            // 行/サイズ変更のArrangeを終えてから可視範囲を読む。確定中の通知も同じ要求へまとめる。
            if (CanObserve())
                TransferList.UpdateLayout();
            viewportQueued = false;
            var scrolled = queuedUserScroll;
            queuedUserScroll = false;
            if (!CanObserve() || ViewModel is not { } model || scroller is null)
                return;
            var panel = TransferList.ItemsPanelRoot as ItemsStackPanel;
            await model.ObserveViewportAsync(panel?.LastVisibleIndex ?? -1, scroller.ScrollableHeight > 0, scrolled);
        });
    }
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            if (Descendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }
    private void TransferListLoaded(object sender, RoutedEventArgs args)
    {
        scroller = Descendant<ScrollViewer>(TransferList);
        if (scroller is not null)
        {
            scroller.ViewChanged -= ViewChanged;
            scroller.ViewChanged += ViewChanged;
            scroller.DirectManipulationStarted -= ListManipulationStarted;
            scroller.DirectManipulationStarted += ListManipulationStarted;
        }
        ObserveViewport();
    }
    private void ListManipulationStarted(object? sender, object args) => scrollIntent = scroller?.ScrollableHeight > 0;
    private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (scroller is null)
            return;
        var user = scrollIntent && scroller.VerticalOffset > previousOffset;
        previousOffset = scroller.VerticalOffset;
        if (!args.IsIntermediate)
            scrollIntent = false;
        ScheduleViewport(user);
    }
    private void TransferSizeChanged(object sender, SizeChangedEventArgs args) => ObserveViewport();
    private void TransferDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, TransferList); element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: TransferListItem row })
            {
                args.Handled = true;
                ViewModel?.EditCommand.Execute(row.TransferId);
                return;
            }
    }
    private void TransferTextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
            return;
        // TextBox/IMEの標準処理後、外側の共通Enter移動へ流さない。
        if (keyboard.IsComposing)
        {
            args.Handled = true;
            return;
        }
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) ||
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            return;
        args.Handled = true;
        ViewModel?.SearchCommand.Execute(null);
    }
}
