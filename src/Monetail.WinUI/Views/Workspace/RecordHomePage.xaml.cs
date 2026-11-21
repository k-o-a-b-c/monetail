using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Workspace;
using Monetail.WinUI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI.Views.Workspace;
public sealed partial class RecordHomePage : Page
{
    public RecordHomeViewModel? ViewModel { get; private set; }

    private readonly FormKeyboard keyboard;
    private CancellationTokenSource? homeResume;
    public event EventHandler<string>? Failed;
    public RecordHomePage()
    {
        InitializeComponent();
        keyboard = new FormKeyboard(RecordFilters, null, null, ReceiptMonth, RecordKeyword);
        Loaded += (_, _) =>
        {
            // 新しいPageの表示位置は、保留中の追加読込の完了を待たず復元する。
            RestorePositions();
            Resume();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = args.Parameter as RecordHomeViewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }

    public void SetViewModel(RecordHomeViewModel? model)
    {
        ViewModel = model;
        Bindings.Update();
        if (model is null)
        {
            // x:Bindは途中の参照がnullになると接続先を更新しないため、購読を明示的に解除する。
            ReceiptsView.ViewModel = null;
            TransfersView.ViewModel = null;
        }
    }

    public void ShowEditor(UIElement? editor)
    {
        if (editor is not null)
            Suspend();
        EditorHost.Content = editor;
        RecordContent.Visibility = editor is null ? Visibility.Visible : Visibility.Collapsed;
        if (editor is null)
            Resume();
    }

    public void RememberPositions()
    {
        ReceiptsView.RememberPosition();
        TransfersView.RememberPosition();
    }

    public void RestorePositions()
    {
        ReceiptsView.RestorePosition();
        TransfersView.RestorePosition();
    }

    public void Resume()
    {
        if (homeResume is not null || !IsLoaded || ViewModel is null || EditorHost.Content is not null)
            return;
        var pending = new CancellationTokenSource();
        homeResume = pending;
        _ = ResumeAsync(pending);
    }

    private bool Current(CancellationTokenSource pending) => !pending.IsCancellationRequested && IsLoaded && EditorHost.Content is null && ViewModel is not null && Visible();
    private bool Visible()
    {
        for (DependencyObject? element = this; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { Visibility: Visibility.Collapsed })
                return false;
        return true;
    }

    private async Task ResumeAsync(CancellationTokenSource pending)
    {
        using (pending)
        {
            try
            {
                await Task.Yield();
                if (!Current(pending))
                    return;
                await ViewModel!.ResumeAsync();
                if (!Current(pending))
                    return;
                ReceiptsView.ObserveViewport();
                TransfersView.ObserveViewport();
            }
            catch (OperationCanceledException)when (pending.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("Record home resume failed: {0}", error.GetType().FullName);
                if (Current(pending))
                    Failed?.Invoke(this, "一覧を表示できませんでした。");
            }
            finally
            {
                if (ReferenceEquals(homeResume, pending))
                    homeResume = null;
            }
        }
    }

    public void Suspend()
    {
        homeResume?.Cancel();
        homeResume = null;
        ViewModel?.Suspend();
    }

    public void Release()
    {
        Suspend();
        RememberPositions();
        Bindings.StopTracking();
        ReceiptsView.ViewModel = null;
        TransfersView.ViewModel = null;
        EditorHost.Content = null;
        ViewModel = null;
        Failed = null;
    }

    private async Task Act(Func<RecordHomeViewModel, Task> operation)
    {
        if (ViewModel is not { } model)
            return;
        try
        {
            await operation(model);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Record search failed: {0}", error.GetType().FullName);
            if (ReferenceEquals(ViewModel, model) && Visible())
                Failed?.Invoke(this, "一覧を表示できませんでした。");
        }

        if (ViewModel?.MonthError is not null)
            ReceiptMonth.Focus(FocusState.Programmatic);
    }

    private async void ApplyRecordMonth(object sender, RoutedEventArgs args) => await Act(m => m.ApplyMonthCommand.ExecuteAsync(null));
    private async void ApplyRecordKeyword(object sender, RoutedEventArgs args) => await Act(m => m.ApplyKeywordCommand.ExecuteAsync(null));
    private async void MoveReceiptMonth(object sender, RoutedEventArgs args) => await Act(m => m.MoveMonthCommand.ExecuteAsync(ReferenceEquals(sender, ReceiptPrevious) ? -1 : 1));
    private async void ReceiptPeriodChanged(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { } model && model.AllPeriods != ReferenceEquals(sender, ReceiptAllPeriods))
            await Act(m => m.SetPeriodCommand.ExecuteAsync(ReferenceEquals(sender, ReceiptAllPeriods)));
    }

    private async void ReceiptOwnerChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel is { } model && ReceiptOwner.SelectedItem is RecordFilterChoice choice && model.Search.Receipts.OwnerPersonId != choice.Id)
            await Act(m => m.SetOwnerCommand.ExecuteAsync(choice));
    }

    private async void ReceiptPaymentChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel is { } model && ReceiptPayment.SelectedItem is RecordFilterChoice choice && model.Search.Receipts.PaymentMethodId != choice.Id)
            await Act(m => m.SetPaymentCommand.ExecuteAsync(choice));
    }

    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private async void ReceiptTextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || keyboard.IsComposing || Down(VirtualKey.Menu) || Down(VirtualKey.Shift) || Down(VirtualKey.Control))
            return;
        args.Handled = true;
        await Act(m => m.ApplyMonthCommand.ExecuteAsync(null));
    }

    private async void RecordKeywordKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
            return;
        if (keyboard.IsComposing)
        {
            args.Handled = true;
            return;
        }

        if (Down(VirtualKey.Menu) || Down(VirtualKey.Shift) || Down(VirtualKey.Control))
            return;
        args.Handled = true;
        await Act(m => m.ApplyKeywordCommand.ExecuteAsync(null));
    }

    private async void RetryReceiptOpen(object sender, RoutedEventArgs args) => await Act(m => m.RetryReceiptCommand.ExecuteAsync(null));
    private async void RetryTransferOpening(object sender, RoutedEventArgs args) => await Act(m => m.RetryTransferCommand.ExecuteAsync(null));
    private Visibility ErrorVisible(string? text) => text is null ? Visibility.Collapsed : Visibility.Visible;
    private bool MonthEnabled(bool all) => !all;
    private Visibility NoticeVisible(RecordOpeningNotice? notice) => notice is null ? Visibility.Collapsed : Visibility.Visible;
    private string NoticeMessage(RecordOpeningNotice? notice) => notice?.Message ?? "";
    private bool NoticeLoading(RecordOpeningNotice? notice) => notice?.Loading == true;
    private Visibility NoticeRetry(RecordOpeningNotice? notice) => notice?.CanRetry == true ? Visibility.Visible : Visibility.Collapsed;
}
