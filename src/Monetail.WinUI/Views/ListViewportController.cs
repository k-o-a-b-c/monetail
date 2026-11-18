using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Presentation.Lists;
using Windows.System;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Monetail.WinUI.Views;

/// <summary>両一覧共通のスクロール操作意図、可視範囲測定、表示位置復元を管理する。</summary>
internal sealed class ListViewportController
{
    private readonly UserControl owner;
    private readonly ListView list;
    private readonly Func<bool> hasModel;
    private readonly Func<int, bool, bool, Task> observe;
    private ScrollViewer? scroller;
    private bool viewportQueued, scrollIntent, queuedUserScroll;
    private double previousOffset;
    private (double Horizontal, double Vertical) returnPosition;

    internal ListViewportController(UserControl owner, ListView list, Func<bool> hasModel,
        Func<int, bool, bool, Task> observe)
    {
        this.owner = owner;
        this.list = list;
        this.hasModel = hasModel;
        this.observe = observe;
        list.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, _) => scrollIntent = scroller?.ScrollableHeight > 0), true);
        list.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var element = args.OriginalSource as DependencyObject; element is not null;
                element = VisualTreeHelper.GetParent(element))
                if (element is ScrollBar)
                {
                    scrollIntent = true;
                    break;
                }
        }), true);
        list.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End or VirtualKey.Space)
                scrollIntent = scroller?.ScrollableHeight > 0;
        }), true);
        list.Loaded += (_, _) => Observe();
        list.SizeChanged += (_, _) => Observe();
    }

    internal void ResetForModel()
    {
        returnPosition = default;
        ResetViewport();
    }

    internal void RowsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            ResetViewport();
        Observe();
    }

    private void ResetViewport()
    {
        IgnoreUserScroll();
        previousOffset = 0;
        scroller?.ChangeView(null, 0, null, true);
    }

    internal void IgnoreUserScroll() => scrollIntent = queuedUserScroll = false;

    internal void RememberPosition() =>
        returnPosition = (scroller?.HorizontalOffset ?? 0, scroller?.VerticalOffset ?? 0);

    internal void RestorePosition() => owner.DispatcherQueue.TryEnqueue(() =>
    {
        if (CanObserve())
        {
            IgnoreUserScroll();
            scroller?.ChangeView(returnPosition.Horizontal, returnPosition.Vertical, null, true);
        }
    });

    internal void ShowSavedSelection<TItem>(CursorListViewModel<TItem>? model,
        Func<CursorListViewModel<TItem>?> current) where TItem : class
    {
        if (model is not { IsFindingSaved: true, SelectedItem: { } selected })
            return;
        owner.DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(current(), model) || !ReferenceEquals(model.SelectedItem, selected))
                return;
            IgnoreUserScroll();
            list.ScrollIntoView(selected);
        });
    }

    internal void Observe() => ScheduleViewport(false);

    private bool CanObserve()
    {
        if (!owner.IsLoaded || !hasModel())
            return false;
        for (DependencyObject? element = owner; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { Visibility: Visibility.Collapsed })
                return false;
        return true;
    }

    private void ScheduleViewport(bool user)
    {
        queuedUserScroll |= user;
        if (viewportQueued || !CanObserve())
            return;
        viewportQueued = true;
        owner.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            // 行/サイズ変更のArrange後に測定し、連続通知を1回へまとめる。
            if (CanObserve())
            {
                list.UpdateLayout();
                ConnectScroller();
            }
            viewportQueued = false;
            var scrolled = queuedUserScroll;
            queuedUserScroll = false;
            if (!CanObserve() || scroller is null)
                return;
            var panel = list.ItemsPanelRoot as ItemsStackPanel;
            await observe(panel?.LastVisibleIndex ?? -1, scroller.ScrollableHeight > 0, scrolled);
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
        var current = Descendant<ScrollViewer>(list);
        if (ReferenceEquals(scroller, current))
            return;
        if (scroller is not null)
        {
            scroller.ViewChanged -= ViewChanged;
            scroller.DirectManipulationStarted -= ListManipulationStarted;
        }
        scroller = current;
        previousOffset = scroller?.VerticalOffset ?? 0;
        IgnoreUserScroll();
        if (scroller is not null)
        {
            scroller.ViewChanged += ViewChanged;
            scroller.DirectManipulationStarted += ListManipulationStarted;
        }
    }

    private void ListManipulationStarted(object? sender, object args) =>
        scrollIntent = scroller?.ScrollableHeight > 0;

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
}
