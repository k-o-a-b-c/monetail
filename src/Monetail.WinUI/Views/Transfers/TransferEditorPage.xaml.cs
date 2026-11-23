using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;
using Windows.System;
using Windows.UI.Core;
namespace Monetail.WinUI.Views.Transfers;

public sealed partial class TransferEditorPage : Page
{
    public TransferEditorViewModel? ViewModel
    {
        get; private set;
    }
    public event Func<Task>? ReturnRequested;
    public event Func<TransferWriteCompletion, bool, Task>? Saved;
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (TransferEditorViewModel)args.Parameter;
        ViewModel.ActionRequested += ExecuteActionAsync;
        ChangeModel();
        Loaded += InitialFocus;
        Populate();
    }
    private void InitialFocus(object sender, RoutedEventArgs args)
    {
        Loaded -= InitialFocus;
        FocusField("transactionAt");
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }
    public Func<DependencyObject?, bool>? IsCancellationTarget
    {
        get;
        set;
    }
    private TransferEditorState? transferEditor => ViewModel?.Editor;
    private bool TransferFrozen => ViewModel?.IsFrozen != false;
    private readonly FormKeyboard transferKeyboard;
    private bool transferRendering, transferRestoringRecoveryView;
    private sealed record EditPosition(Control? Focus, int SelectionStart, int SelectionLength, double ScrollOffset);
    private EditPosition? position;
    private string SuggestionError
    {
        set
        {
            if (ViewModel is { } model)
                model.SuggestionError = value;
        }
    }
    public TransferEditorPage()
    {
        InitializeComponent();
        transferKeyboard = new FormKeyboard(this, TransferSave,
            () => ViewModel?.SaveCommand.ExecuteAsync(null) ?? Task.CompletedTask, TransferDate, TransferTime, TransferAmount, TransferMemo);
        TransferName.Loaded += ObserveNameInput;
        suggestionCallback = TransferName.RegisterPropertyChangedCallback(AutoSuggestBox.IsSuggestionListOpenProperty, SuggestionsClosed);
        TransferDate.IsCancellationTarget = TransferTime.IsCancellationTarget = element =>
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, TransferBack) || IsCancellationTarget?.Invoke(element) == true)
                    return true;
            return false;
        };
    }
    private readonly long suggestionCallback;
    private TextBox? nameInput;
    private void ObserveNameInput(object sender, RoutedEventArgs args)
    {
        if (nameInput is not null || Descendant<TextBox>(TransferName) is not { } input)
            return;
        nameInput = input;
        transferKeyboard.ObserveInput(input);
        input.TextCompositionStarted += NameCompositionStarted;
        input.TextCompositionEnded += NameCompositionEnded;
    }
    private void NameCompositionStarted(object sender, object args) => transferNameCompositionPending = true;
    private void NameCompositionEnded(object sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (ViewModel is not null && !transferKeyboard.IsComposing && transferNameCompositionPending)
        {
            transferNameCompositionPending = false;
            _ = SearchTransferNamesAsync(true);
        }
    });
    private void SuggestionsClosed(DependencyObject sender, DependencyProperty property)
    {
        if (TransferName.IsSuggestionListOpen || transferClosingSuggestions || transferRendering || transferEditor is null)
            return;
        RestoreTransferTypedName();
        transferSuggestionsSuppressed = true;
        CancelTransferSuggestions();
    }
    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found)
            return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Descendant<T>(VisualTreeHelper.GetChild(root, i)) is { } item)
                return item;
        return null;
    }
    private void ChangeModel()
    {
        ResetTransferSuggestions();
        transferAutofillCancellation?.Cancel();
        Bindings.Update();
    }
    private Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Editable(bool frozen) => Show(!frozen);
    private Visibility HasText(string text) => Show(!string.IsNullOrEmpty(text));
    private InfoBarSeverity NoticeSeverity(TransferNoticeLevel level) => level switch
    {
        TransferNoticeLevel.Information => InfoBarSeverity.Informational,
        TransferNoticeLevel.Warning => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Error
    };
    private void Populate()
    {
        if (ViewModel is not { } model)
            return;
        var state = model.Editor;
        transferRendering = true;
        TransferDate.Text = DateTimeInput.FormatDate(state.Draft.TransactionDate);
        TransferTime.Text = DateTimeInput.FormatTime(state.Draft.TransactionTime);
        TransferName.Text = state.Draft.Name;
        TransferAmount.Text = state.Draft.Amount?.ToString(CultureInfo.InvariantCulture) ?? "";
        TransferMemo.Text = state.Draft.Memo ?? "";
        model.NoticeOpen = false;
        transferRendering = false;
        RefreshChoices();
        RefreshPresentation();
    }
    private void SynchronizeInput()
    {
        if (!transferRendering && ViewModel is { } model)
            model.UpdateInput(TransferDate.Text, TransferTime.Text, TransferAmount.Text, TransferMemo.Text);
    }
    private void CompleteInput()
    {
        TransferDate.Complete();
        TransferTime.Complete();
        SynchronizeInput();
    }
    private void RefreshChoices()
    {
        if (ViewModel is not { } model || model.PaymentChoices() is not { } choices)
            return;
        transferRendering = true;
        void Fill(ComboBox combo, TransferPaymentChoice[] items, Guid? selected)
        {
            combo.ItemsSource = items;
            combo.SelectedItem = items.FirstOrDefault(p => p.Id == selected);
        }
        Fill(TransferFrom, choices.From, model.Editor.Draft.From.PaymentMethodId);
        Fill(TransferTo, choices.To, model.Editor.Draft.To.PaymentMethodId);
        transferRendering = false;
    }
    private void RefreshPresentation() => ViewModel?.Refresh();
    private void TransferTextChanged(object sender, TextChangedEventArgs args)
    {
        if (transferRendering)
            return;
        SynchronizeInput();
        if (ReferenceEquals(sender, TransferDate))
        {
            transferAutofillCancellation?.Cancel();
            transferAutofillKey = null;
            RefreshChoices();
        }
        RefreshPresentation();
    }
    private void TransferPaymentChanged(object sender, SelectionChangedEventArgs args)
    {
        if (transferRendering || TransferFrozen || transferEditor is not { } state ||
            (sender as ComboBox)?.SelectedItem is not TransferPaymentChoice { Id: var id })
            return;
        if (ReferenceEquals(sender, TransferFrom))
            state.SelectFrom(id);
        else
            state.SelectTo(id);
        RefreshPresentation();
        if (ReferenceEquals(sender, TransferFrom))
            _ = AutofillTransferAsync();
    }
    private void SetDialogOpen(bool open)
    {
        TransferDate.SuppressCompletion = TransferTime.SuppressCompletion = open;
        if (open)
        {
            CancelTransferSuggestions();
            RestoreTransferTypedName();
        }
    }
    private void CancelRequests()
    {
        CancelTransferSuggestions();
        transferAutofillCancellation?.Cancel();
    }
    private void Suspend()
    {
        SetDialogOpen(true);
        transferSuggestions.Clear();
        TransferName.ItemsSource = null;
        transferAutofillCancellation?.Cancel();
        transferAutofillKey = null;
    }
    private void Discard()
    {
        SetDialogOpen(false);
        ResetTransferSuggestions();
        transferAutofillCancellation?.Cancel();
        transferAutofillCancellation?.Dispose();
        transferAutofillCancellation = null;
        transferRendering = true;
        TransferDate.Text = TransferTime.Text = TransferAmount.Text = TransferMemo.Text = TransferName.Text = TransferNameReadOnly.Text = "";
        TransferFrom.ItemsSource = TransferTo.ItemsSource = null;
        TransferFromReadOnly.Text = TransferToReadOnly.Text = "";
        transferRendering = false;
        TransferInputScroller.ChangeView(null, 0, null, true);
        position = null;
    }
    private void RememberPosition()
    {
        if (ViewModel?.RecoveryBlocked == true && position is not null)
            return;
        var focus = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;
        position = new(focus, (focus as TextBox)?.SelectionStart ?? 0, (focus as TextBox)?.SelectionLength ?? 0,
            TransferInputScroller.VerticalOffset);
    }
    private void RestorePosition(Func<bool> current)
    {
        var model = ViewModel;
        var saved = position;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!current() || !ReferenceEquals(model, ViewModel) || saved is null)
                return;
            TransferInputScroller.UpdateLayout();
            transferRestoringRecoveryView = true;
            try
            {
                if (saved.Focus is { IsLoaded: true, IsEnabled: true, Visibility: Visibility.Visible } focus)
                {
                    focus.Focus(FocusState.Programmatic);
                    if (focus is TextBox text)
                        text.Select(saved.SelectionStart, saved.SelectionLength);
                }
                TransferInputScroller.ChangeView(null, saved.ScrollOffset, null, true);
            }
            finally
            {
                transferRestoringRecoveryView = false;
                SetDialogOpen(false);
            }
        });
    }
    private void FocusField(string field)
    {
        Control target = field switch
        {
            "transactionAt" => TransferDate,
            "name" => TransferName,
            "fromPaymentMethodId" => TransferFrom,
            "toPaymentMethodId" => TransferTo,
            "amount" => TransferAmount,
            "memo" => TransferMemo,
            _ => TransferName
        };
        target.Focus(FocusState.Programmatic);
    }
    private readonly Dictionary<string, TransferSuggestionsInfo> transferSuggestions = new(StringComparer.Ordinal);
    private CancellationTokenSource? transferSuggestionCancellation, transferAutofillCancellation;
    private long transferSuggestionGeneration;
    private bool transferClosingSuggestions, transferSuggestionsSuppressed, transferNameFocusTransition, transferNameCompositionPending, transferNameKeyboardSubmit, transferNameKeyboardHadSuggestions;
    private string? transferNamePreview;
    private (string Name, DateOnly Day, Guid? From)? transferAutofillKey;
    private static bool TransferKeyDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void ResetTransferSuggestions()
    {
        CancelTransferSuggestions();
        TransferName.ItemsSource = null;
        transferSuggestions.Clear();
        transferNamePreview = null;
        transferAutofillKey = null;
        transferSuggestionsSuppressed = false;
        SuggestionError = "";
    }
    private void CancelTransferSuggestions()
    {
        transferSuggestionGeneration++;
        transferSuggestionCancellation?.Cancel();
        transferSuggestionCancellation?.Dispose();
        transferSuggestionCancellation = null;
        transferClosingSuggestions = true;
        try
        {
            TransferName.IsSuggestionListOpen = false;
        }
        finally
        {
            transferClosingSuggestions = false;
        }
    }
    private void TransferNameChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (transferRendering || TransferFrozen || transferEditor is not { } editor || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        transferAutofillCancellation?.Cancel();
        transferAutofillKey = null;
        editor.Draft.Name = sender.Text;
        transferNamePreview = null;
        transferSuggestionsSuppressed = false;
        RefreshPresentation();
        if (!transferKeyboard.IsComposing)
            _ = SearchTransferNamesAsync(true);
    }
    private void TransferNameFocused(object sender, RoutedEventArgs args)
    {
        if (TransferFrozen || transferRestoringRecoveryView || transferEditor is null)
            return;
        transferSuggestionsSuppressed = false;
        var editor = transferEditor;
        var text = editor.Draft.Name;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(editor, transferEditor) && editor.Draft.Name == text && !transferSuggestionsSuppressed && !TransferFrozen && TransferNameHasFocus())
                _ = SearchTransferNamesAsync(false);
        });
    }
    private bool TransferNameHasFocus()
    {
        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, TransferName))
                return true;
        return false;
    }
    private async Task SearchTransferNamesAsync(bool debounce)
    {
        if (TransferFrozen || transferKeyboard.IsComposing || transferSuggestionsSuppressed || transferEditor is not { } editor)
            return;
        CancelTransferSuggestions();
        TransferName.ItemsSource = null;
        var epoch = transferSuggestionGeneration;
        var text = editor.Draft.Name;
        SuggestionError = "";
        if (text.Length > 100)
            return;
        var model = ViewModel!;
        using var cancellation = new CancellationTokenSource();
        transferSuggestionCancellation = cancellation;
        bool Current() => epoch == transferSuggestionGeneration && !cancellation.IsCancellationRequested && ReferenceEquals(transferEditor, editor) && ReferenceEquals(ViewModel, model) &&
            !TransferFrozen && !transferSuggestionsSuppressed && !transferKeyboard.IsComposing && TransferNameHasFocus() && editor.Draft.Name == text;
        try
        {
            if (debounce)
                await Task.Delay(250, cancellation.Token);
            if (!Current())
                return;
            if (!transferSuggestions.TryGetValue(text, out var result))
            {
                result = await model.SuggestAsync(new(text), cancellation.Token);
                if (Current())
                    transferSuggestions[text] = result;
            }
            if (!Current())
                return;
            TransferName.ItemsSource = result.Suggestions;
            TransferName.IsSuggestionListOpen = result.Suggestions.Length != 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is TransferRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                TransferName.ItemsSource = null;
                TransferName.IsSuggestionListOpen = false;
                SuggestionError = "候補を取得できませんでした";
            }
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                StopTransferAuthentication();
        }
        finally
        {
            if (ReferenceEquals(transferSuggestionCancellation, cancellation))
                transferSuggestionCancellation = null;
        }
    }
    private void TransferSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        transferNamePreview = args.SelectedItem as string;
    }
    private void TransferNameQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (TransferFrozen || transferKeyboard.IsComposing || transferEditor is not { } editor)
            return;
        var keyboard = transferNameKeyboardSubmit;
        transferNameKeyboardSubmit = false;
        var selected = !keyboard || transferNameKeyboardHadSuggestions ? args.ChosenSuggestion as string : null;
        if (!keyboard && selected is null)
            return;
        editor.Draft.Name = selected ?? (transferNamePreview is null ? args.QueryText ?? editor.Draft.Name : editor.Draft.Name);
        RestoreTransferTypedName();
        DismissTransferNames();
        RefreshPresentation();
        if (keyboard)
        {
            transferNameFocusTransition = true;
            TransferFrom.Focus(FocusState.Programmatic);
            transferNameFocusTransition = false;
        }
        _ = AutofillTransferAsync();
    }
    private void RestoreTransferTypedName()
    {
        if (transferEditor is not { } editor)
            return;
        transferRendering = true;
        TransferName.Text = editor.Draft.Name;
        transferNamePreview = null;
        transferRendering = false;
    }
    private void DismissTransferNames()
    {
        transferSuggestionsSuppressed = true;
        CancelTransferSuggestions();
    }
    private void TransferNameLostFocus(object sender, RoutedEventArgs args)
    {
        if (TransferFrozen || transferEditor is null)
            return;
        RestoreTransferTypedName();
        DismissTransferNames();
        if (!transferNameFocusTransition)
            _ = AutofillTransferAsync();
    }
    private void TransferNamePreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (transferKeyboard.IsComposing || TransferFrozen || transferEditor is not { } editor)
            return;
        if (args.Key == VirtualKey.Escape && TransferName.IsSuggestionListOpen)
        {
            args.Handled = true;
            RestoreTransferTypedName();
            DismissTransferNames();
            return;
        }
        if (args.Key == VirtualKey.Tab || args.Key == VirtualKey.Enter && TransferKeyDown(VirtualKey.Shift))
        {
            args.Handled = true;
            RestoreTransferTypedName();
            DismissTransferNames();
            FocusManager.TryMoveFocus(TransferKeyDown(VirtualKey.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = XamlRoot.Content });
            return;
        }
        if (args.Key != VirtualKey.Enter || TransferKeyDown(VirtualKey.Menu))
            return;
        if (TransferKeyDown(VirtualKey.Control))
        {
            args.Handled = true;
            if (!TransferName.IsSuggestionListOpen && TransferSave.IsEnabled)
                _ = ViewModel!.SaveCommand.ExecuteAsync(null);
            return;
        }
        // 実際の選択項目の確定は標準AutoSuggestBoxのQuerySubmittedに任せる。
        transferNameKeyboardSubmit = true;
        transferNameKeyboardHadSuggestions = TransferName.IsSuggestionListOpen;
    }
    private void TransferNameKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (transferNameCompositionPending && !transferKeyboard.IsComposing)
        {
            transferNameCompositionPending = false;
            if (!transferSuggestionsSuppressed && !TransferFrozen)
                _ = SearchTransferNamesAsync(true);
        }
    }
    private async Task AutofillTransferAsync()
    {
        if (TransferFrozen || transferEditor is not { } editor || editor.IsExisting || editor.Draft.TransactionDate is not DateOnly date || editor.Draft.UnresolvedDateText is not null)
            return;
        var key = (editor.Draft.Name, date, editor.Draft.From.Source == Monetail.Windows.Client.Transfers.TransferSelectionSource.Manual ? editor.Draft.From.PaymentMethodId : null);
        if (transferAutofillKey == key)
            return;
        var context = editor.BeginAutofill();
        if (context is null)
            return;
        transferAutofillKey = key;
        transferAutofillCancellation?.Cancel();
        transferAutofillCancellation?.Dispose();
        using var cancellation = new CancellationTokenSource();
        transferAutofillCancellation = cancellation;
        var model = ViewModel!;
        try
        {
            var result = await model.AutofillAsync(new(context.Name, context.TransactionDate, context.FromPaymentMethodId), cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(editor, transferEditor) && ReferenceEquals(ViewModel, model) && !TransferFrozen && editor.TryApplyAutofill(context, result))
            {
                SuggestionError = "";
                RefreshChoices();
                RefreshPresentation();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is TransferRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (ReferenceEquals(editor, transferEditor) && !cancellation.IsCancellationRequested)
                SuggestionError = "支払方法を補完できませんでした。手動で選択できます。";
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (ReferenceEquals(editor, transferEditor) && !cancellation.IsCancellationRequested)
                StopTransferAuthentication();
        }
        finally
        {
            if (ReferenceEquals(transferAutofillCancellation, cancellation))
                transferAutofillCancellation = null;
        }
    }
    private void StopTransferAuthentication()
    {
        ViewModel?.RequireAuthentication();
    }
    private void TransferDateConfirmed(object sender, RoutedEventArgs args)
    {
        if (!TransferFrozen)
        {
            SynchronizeInput();
            RefreshChoices();
            RefreshPresentation();
            _ = AutofillTransferAsync();
        }
    }
}
