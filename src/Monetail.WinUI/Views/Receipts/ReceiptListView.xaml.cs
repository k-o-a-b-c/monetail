using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.Windows.Presentation.Receipts;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Monetail.WinUI.Views.Receipts;

public sealed partial class ReceiptListView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ReceiptListViewModel), typeof(ReceiptListView),
        new PropertyMetadata(null, (owner, args) => ((ReceiptListView)owner).ChangeModel(
            args.OldValue as ReceiptListViewModel, args.NewValue as ReceiptListViewModel)));
    public ReceiptListViewModel? ViewModel
    {
        get => (ReceiptListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public static readonly DependencyProperty OpeningContentProperty = DependencyProperty.Register(
        nameof(OpeningContent), typeof(UIElement), typeof(ReceiptListView), new PropertyMetadata(null));
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

    public ReceiptListView()
    {
        InitializeComponent();
        keyboard = new FormKeyboard(this, null, null, ReceiptShop);
        ReceiptList.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => scrollIntent = scroller?.ScrollableHeight > 0), true);
        ReceiptList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is ScrollBar)
                {
                    scrollIntent = true;
                    break;
                }
        }), true);
        ReceiptList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End or VirtualKey.Space)
                scrollIntent = scroller?.ScrollableHeight > 0;
        }), true);
    }
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void ChangeModel(ReceiptListViewModel? previous, ReceiptListViewModel? current)
    {
        if (previous is not null)
        {
            ((INotifyCollectionChanged)previous.Rows).CollectionChanged -= RowsChanged;
        }
        returnPosition = default;
        ResetViewport();
        if (current is not null)
        {
            ((INotifyCollectionChanged)current.Rows).CollectionChanged += RowsChanged;
        }
        Bindings.Update();
        ObserveViewport();
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
        if (!IsLoaded || ViewModel is null)
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
            {
                ReceiptList.UpdateLayout();
                ConnectScroller();
            }
            viewportQueued = false;
            var scrolled = queuedUserScroll;
            queuedUserScroll = false;
            if (!CanObserve() || ViewModel is not { } model || scroller is null)
                return;
            var panel = ReceiptList.ItemsPanelRoot as ItemsStackPanel;
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
    private void ConnectScroller()
    {
        var current = Descendant<ScrollViewer>(ReceiptList);
        if (ReferenceEquals(scroller, current))
            return;
        if (scroller is not null)
        {
            scroller.ViewChanged -= ViewChanged;
            scroller.DirectManipulationStarted -= ListManipulationStarted;
        }
        scroller = current;
        previousOffset = scroller?.VerticalOffset ?? 0;
        scrollIntent = queuedUserScroll = false;
        if (scroller is not null)
        {
            scroller.ViewChanged += ViewChanged;
            scroller.DirectManipulationStarted += ListManipulationStarted;
        }
    }
    private void ReceiptListLoaded(object sender, RoutedEventArgs args) => ObserveViewport();
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
    private void ReceiptSizeChanged(object sender, SizeChangedEventArgs args) => ObserveViewport();
    private void ReceiptDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, ReceiptList); element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: ReceiptListItem row })
            {
                args.Handled = true;
                ViewModel?.EditCommand.Execute(row.ReceiptId);
                return;
            }
    }
    private void ReceiptTextKeyDown(object sender, KeyRoutedEventArgs args)
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
