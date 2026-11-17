using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, TransferSuggestionsInfo> transferSuggestions = new(StringComparer.Ordinal);
    private CancellationTokenSource? transferSuggestionCancellation, transferAutofillCancellation;
    private long transferSuggestionGeneration;
    private bool transferClosingSuggestions, transferSuggestionsSuppressed, transferNameFocusTransition, transferNameInputObserved, transferNameCompositionPending, transferNameKeyboardSubmit, transferNameKeyboardHadSuggestions;
    private string? transferNamePreview;
    private (string Name, DateOnly Day, Guid? From)? transferAutofillKey;
    private static bool TransferKeyDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void ResetTransferSuggestions()
    { CancelTransferSuggestions(); TransferName.ItemsSource = null; transferSuggestions.Clear(); transferNamePreview = null; transferAutofillKey = null; transferSuggestionsSuppressed = false; TransferSuggestionError.Text = ""; }
    private void CancelTransferSuggestions()
    {
        transferSuggestionGeneration++; transferSuggestionCancellation?.Cancel(); transferSuggestionCancellation?.Dispose(); transferSuggestionCancellation = null;
        transferClosingSuggestions = true;
        try { TransferName.IsSuggestionListOpen = false; }
        finally { transferClosingSuggestions = false; }
    }
    private void TransferNameChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (transferRendering || TransferFrozen || transferEditor is not { } editor || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        transferAutofillCancellation?.Cancel(); transferAutofillKey = null; editor.Draft.Name = sender.Text; transferNamePreview = null; transferSuggestionsSuppressed = false; RenderTransferEditor();
        if (!transferKeyboard.IsComposing) _ = SearchTransferNamesAsync(true);
    }
    private void TransferNameFocused(object sender, RoutedEventArgs args)
    {
        if (TransferFrozen || transferRestoringRecoveryView || transferEditor is null) return;
        transferSuggestionsSuppressed = false; var editor = transferEditor; var text = editor.Draft.Name;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(editor, transferEditor) && editor.Draft.Name == text && !transferSuggestionsSuppressed && !TransferFrozen && TransferNameHasFocus()) _ = SearchTransferNamesAsync(false);
        });
    }
    private bool TransferNameHasFocus()
    {
        for (var element = FocusManager.GetFocusedElement(WorkspaceRoot.XamlRoot) as DependencyObject; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, TransferName)) return true;
        return false;
    }
    private async Task SearchTransferNamesAsync(bool debounce)
    {
        if (TransferFrozen || transferKeyboard.IsComposing || transferSuggestionsSuppressed || transferEditor is not { } editor || currentLedger is null) return;
        CancelTransferSuggestions(); TransferName.ItemsSource = null; var epoch = transferSuggestionGeneration; var text = editor.Draft.Name;
        TransferSuggestionError.Text = ""; if (text.Length > 100) return;
        var ledger = currentLedger.LedgerId; var person = account?.AuthUserId;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transferSuggestionCancellation = cancellation;
        bool Current() => epoch == transferSuggestionGeneration && !cancellation.IsCancellationRequested && ReferenceEquals(transferEditor, editor) && currentLedger?.LedgerId == ledger && account?.AuthUserId == person &&
            !TransferFrozen && !transferSuggestionsSuppressed && !transferKeyboard.IsComposing && TransferNameHasFocus() && editor.Draft.Name == text;
        try
        {
            if (debounce) await Task.Delay(250, cancellation.Token);
            if (!Current()) return;
            if (!transferSuggestions.TryGetValue(text, out var result))
            { result = await new TransfersWorkspace(connection.Api, connection.Session, ledger).SuggestAsync(new(text), cancellation.Token); if (Current()) transferSuggestions[text] = result; }
            if (!Current()) return;
            TransferName.ItemsSource = result.Suggestions; TransferName.IsSuggestionListOpen = result.Suggestions.Length != 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is TransferRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        { if (Current()) { TransferName.ItemsSource = null; TransferName.IsSuggestionListOpen = false; TransferSuggestionError.Text = "候補を取得できませんでした"; } }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) StopTransferAuthentication(); }
        finally { if (ReferenceEquals(transferSuggestionCancellation, cancellation)) transferSuggestionCancellation = null; }
    }
    private void TransferSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    { transferNamePreview = args.SelectedItem as string; }
    private void TransferNameQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (TransferFrozen || transferKeyboard.IsComposing || transferEditor is not { } editor) return;
        var keyboard = transferNameKeyboardSubmit; transferNameKeyboardSubmit = false;
        var selected = !keyboard || transferNameKeyboardHadSuggestions ? args.ChosenSuggestion as string : null;
        if (!keyboard && selected is null) return;
        editor.Draft.Name = selected ?? (transferNamePreview is null ? args.QueryText ?? editor.Draft.Name : editor.Draft.Name); RestoreTransferTypedName(); DismissTransferNames(); RenderTransferEditor();
        if (keyboard) { transferNameFocusTransition = true; TransferFrom.Focus(FocusState.Programmatic); transferNameFocusTransition = false; }
        _ = AutofillTransferAsync();
    }
    private void RestoreTransferTypedName()
    {
        if (transferEditor is not { } editor) return;
        transferRendering = true; TransferName.Text = editor.Draft.Name; transferNamePreview = null; transferRendering = false;
    }
    private void DismissTransferNames() { transferSuggestionsSuppressed = true; CancelTransferSuggestions(); }
    private void TransferNameLostFocus(object sender, RoutedEventArgs args)
    {
        if (TransferFrozen || transferEditor is null) return;
        RestoreTransferTypedName(); DismissTransferNames();
        if (!transferNameFocusTransition) _ = AutofillTransferAsync();
    }
    private void TransferNamePreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (transferKeyboard.IsComposing || TransferFrozen || transferEditor is not { } editor) return;
        if (args.Key == VirtualKey.Escape && TransferName.IsSuggestionListOpen)
        { args.Handled = true; RestoreTransferTypedName(); DismissTransferNames(); return; }
        if (args.Key == VirtualKey.Tab || args.Key == VirtualKey.Enter && TransferKeyDown(VirtualKey.Shift))
        {
            args.Handled = true; RestoreTransferTypedName(); DismissTransferNames();
            FocusManager.TryMoveFocus(TransferKeyDown(VirtualKey.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = WorkspaceRoot }); return;
        }
        if (args.Key != VirtualKey.Enter || TransferKeyDown(VirtualKey.Menu)) return;
        if (TransferKeyDown(VirtualKey.Control)) { args.Handled = true; if (!TransferName.IsSuggestionListOpen && TransferSave.IsEnabled) _ = SaveTransferAsync(false); return; }
        // 実際の選択項目の確定は標準AutoSuggestBoxのQuerySubmittedに任せる。
        transferNameKeyboardSubmit = true; transferNameKeyboardHadSuggestions = TransferName.IsSuggestionListOpen;
    }
    private void TransferNameKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (transferNameCompositionPending && !transferKeyboard.IsComposing)
        { transferNameCompositionPending = false; if (!transferSuggestionsSuppressed && !TransferFrozen) _ = SearchTransferNamesAsync(true); }
    }
    private async Task AutofillTransferAsync()
    {
        if (TransferFrozen || transferEditor is not { } editor || currentLedger is null || editor.IsExisting || editor.Draft.TransactionDate is not DateOnly date || editor.Draft.UnresolvedDateText is not null) return;
        var key = (editor.Draft.Name, date, editor.Draft.From.Source == Monetail.Windows.Client.Transfers.TransferSelectionSource.Manual ? editor.Draft.From.PaymentMethodId : null);
        if (transferAutofillKey == key) return;
        var context = editor.BeginAutofill(); if (context is null) return;
        transferAutofillKey = key; transferAutofillCancellation?.Cancel(); transferAutofillCancellation?.Dispose();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transferAutofillCancellation = cancellation;
        var ledger = currentLedger.LedgerId; var person = account?.AuthUserId;
        try
        {
            var result = await new TransfersWorkspace(connection.Api, connection.Session, ledger).AutofillAsync(new(context.Name, context.TransactionDate, context.FromPaymentMethodId), cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(editor, transferEditor) && currentLedger?.LedgerId == ledger && account?.AuthUserId == person && !TransferFrozen && editor.TryApplyAutofill(context, result))
            { TransferSuggestionError.Text = ""; RefreshTransferEditorChoices(); RenderTransferEditor(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is TransferRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        { if (ReferenceEquals(editor, transferEditor) && !cancellation.IsCancellationRequested) TransferSuggestionError.Text = "支払方法を補完できませんでした。手動で選択できます。"; }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (ReferenceEquals(editor, transferEditor) && !cancellation.IsCancellationRequested) StopTransferAuthentication(); }
        finally { if (ReferenceEquals(transferAutofillCancellation, cancellation)) transferAutofillCancellation = null; }
    }
    private void StopTransferAuthentication()
    { NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。"); }
    private void TransferDateConfirmed(object sender, RoutedEventArgs args)
    { if (!TransferFrozen) { SyncTransferInput(); RefreshTransferEditorChoices(); RenderTransferEditor(); _ = AutofillTransferAsync(); } }
}
