using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Input;
using Monetail.WinUI.Views;
using Monetail.Windows.Presentation.Transfers;
using Windows.System;
using Windows.UI.Core;

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
    private readonly ListViewportController viewport;

    public TransferListView()
    {
        InitializeComponent();
        keyboard = new FormKeyboard(this, null, null, TransferNameSearch);
        viewport = new(this, TransferList, () => ViewModel is not null,
            (last, filled, user) => ViewModel?.ObserveViewportAsync(last, filled, user) ?? Task.CompletedTask);
    }

    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void ChangeModel(TransferListViewModel? previous, TransferListViewModel? current)
    {
        if (previous is not null)
        {
            previous.PropertyChanged -= ModelChanged;
            ((INotifyCollectionChanged)previous.Rows).CollectionChanged -= RowsChanged;
        }
        viewport.ResetForModel();
        if (current is not null)
        {
            current.PropertyChanged += ModelChanged;
            ((INotifyCollectionChanged)current.Rows).CollectionChanged += RowsChanged;
        }
        Bindings.Update();
        viewport.Observe();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TransferListViewModel.SelectedItem))
            viewport.ShowSavedSelection(ViewModel, () => ViewModel);
    }

    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs args) => viewport.RowsChanged(args);

    public void RememberPosition() => viewport.RememberPosition();
    public void RestorePosition() => viewport.RestorePosition();
    public void ObserveViewport() => viewport.Observe();

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
