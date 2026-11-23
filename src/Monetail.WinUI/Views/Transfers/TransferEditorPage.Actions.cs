using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Transfers;

namespace Monetail.WinUI.Views.Transfers;

public sealed partial class TransferEditorPage
{
    private ContentDialog? confirmation;
    private bool transferLeaving;
    public bool HasDialog => confirmation is not null;
    private Task ExecuteActionAsync(TransferEditorAction action) => action switch
    {
        TransferEditorAction.Back => ReturnRequested?.Invoke() ?? Task.CompletedTask,
        TransferEditorAction.Save => SaveTransferAsync(false),
        TransferEditorAction.Reuse => SaveTransferAsync(true),
        TransferEditorAction.Delete => DeleteTransferAsync(),
        TransferEditorAction.RetryWrite => RetryTransferWriteAsync(),
        TransferEditorAction.RetryMasters => RetryMastersAsync(),
        TransferEditorAction.LoadLatest => LoadLatestTransferAsync(),
        _ => Task.CompletedTask
    };
    public event Func<Task>? RetryMastersRequested;
    private Task RetryMastersAsync() => RetryMastersRequested?.Invoke() ?? Task.CompletedTask;
    private TransferSavePreparation PrepareTransfer(bool reuse) => ViewModel?.PrepareSave(reuse)
        ?? throw new TransferInputException("master", "マスタ情報を取得してください。");
    private void TransferNotice(string message) => ViewModel?.ShowNotice(message);
    private async Task<ContentDialogResult> ConfirmTransferAsync(string title, string message, string primary, string secondary = "", string close = "キャンセル")
    {
        if (HasDialog || ViewModel is not { } model || XamlRoot is null)
            return ContentDialogResult.None;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close
        };
        confirmation = dialog;
        model.SetDialogOpen(true);
        SetDialogOpen(true);
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            confirmation = null;
            model.SetDialogOpen(false);
            SetDialogOpen(false);
            ViewModel?.Refresh();
        }
    }
    private async Task<bool> SendPendingTransferAsync()
    {
        if (ViewModel is not { } model)
            return false;
        CancelRequests();
        var result = await model.SendPendingAsync();
        if (!ReferenceEquals(model, ViewModel))
            return false;
        if (!TransferFrozen && result.Field is not null)
            FocusField(result.Field);
        if (result.OfferLatest)
            await OfferTransferConflictAsync(model.Editor);
        if (result.Completion is not { } completion)
            return false;
        return Saved is { } saved && await saved(completion);
    }
    private async Task<bool> SaveTransferAsync(bool reuse)
    {
        if (transferEditor is not { } state || ViewModel is not { } model || ViewModel?.Context is null ||
            TransferFrozen || HasDialog || model.IsPreparingSave)
            return false;
        // TextChangedは非同期。プレビュー表示を読み戻さず、利用者入力の通知を先に処理する。
        model.SetPreparingSave(true);
        try
        {
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => { delivered.TrySetResult(true); }))
                return false;
            await delivered.Task;
        }
        finally
        {
            model.SetPreparingSave(false);
        }
        if (ViewModel?.Context is null || !ReferenceEquals(state, transferEditor) || TransferFrozen)
            return false;
        CompleteInput();
        TransferSavePreparation prepared;
        try
        {
            prepared = PrepareTransfer(reuse);
        }
        catch (TransferInputException error) { TransferNotice(error.Message); FocusField(error.Field); return false; }
        var warnings = new List<string>();
        if (reuse)
            warnings.Add("現在の入力内容を別の振替として登録します。元の振替は変更しません。");
        else if (state.IsExisting)
            warnings.Add("この振替を更新しますか？");
        if (!reuse && state.HasSavedDateChanged == true)
            warnings.Add("保存済みの日付から取引日が変更されています。");
        if (state.IsMidnight == true)
            warnings.Add("時刻が00:00:00です。このまま保存しますか？");
        if (warnings.Count != 0 && await ConfirmTransferAsync(reuse ? "流用登録の確認" : state.IsExisting ? "更新の確認" : "時刻の確認", string.Join("\n", warnings), reuse ? "流用登録" : state.IsExisting ? "更新" : "登録") != ContentDialogResult.Primary)
            return false;
        if (!ReferenceEquals(state, transferEditor) || TransferFrozen || ViewModel?.Context is null)
            return false;
        model.PrepareWrite(model.Context!.LedgerId, prepared);
        return await SendPendingTransferAsync();
    }
    private async Task DeleteTransferAsync()
    {
        if (transferEditor is not { } state || ViewModel is not { } model || ViewModel?.Context is null ||
            TransferFrozen || HasDialog || model.IsPreparingSave)
            return;
        if (await ConfirmTransferAsync("振替の削除", $"「{state.Draft.Name}」を削除しますか？", "削除") != ContentDialogResult.Primary || !ReferenceEquals(state, transferEditor) || TransferFrozen)
            return;
        model.PrepareDeleteWrite(model.Context!.LedgerId);
        await SendPendingTransferAsync();
    }
    private async Task RetryTransferWriteAsync()
    {
        if (ViewModel?.CanRetryPendingWrite == true && ViewModel?.IsSending == false)
            await SendPendingTransferAsync();
    }
    private async Task OfferTransferConflictAsync(TransferEditorState editor)
    {
        if (!ReferenceEquals(editor, transferEditor) || ViewModel?.CanAdoptLatest != true || HasDialog)
            return;
        var choice = await ConfirmTransferAsync("振替が更新されています", "他の端末で更新されています。現在の入力は保持しています。", "最新を読み込む", close: "編集画面に戻る");
        if (choice == ContentDialogResult.Primary)
            await AdoptLatestTransferAsync(editor);
    }
    private async Task LoadLatestTransferAsync()
    {
        if (transferEditor is { } editor)
            await AdoptLatestTransferAsync(editor);
    }
    private async Task AdoptLatestTransferAsync(TransferEditorState editor)
    {
        if (!ReferenceEquals(editor, transferEditor) || ViewModel?.CanAdoptLatest != true || HasDialog ||
            ViewModel?.IsSending != false || ViewModel?.Context is null)
            return;
        var ledgerId = ViewModel!.Context!.LedgerId;
        var accountId = ViewModel?.Context?.AccountId;
        var choice = await ConfirmTransferAsync("現在の入力を破棄しますか？", "現在の入力内容を破棄して、サーバーの最新情報を読み込みますか？", "最新を読み込む");
        if (choice != ContentDialogResult.Primary || ViewModel?.Context is null || !ReferenceEquals(editor, transferEditor) || ViewModel?.CanAdoptLatest != true || ViewModel?.Context?.LedgerId != ledgerId || ViewModel?.Context?.AccountId != accountId)
            return;
        // 応答内の検証済みlatestだけを採用し、詳細GETや保存を行わない。
        editor.AdoptLatest(true);
        if (ViewModel?.CurrentMethods is { } methods && editor.Draft.From.PaymentMethodId is Guid from && editor.Draft.To.PaymentMethodId is Guid to &&
            (!TransferEditorRules.CanUse(methods, from) || !TransferEditorRules.CanUse(methods, to)))
            editor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
        Populate();
    }
    public async Task<bool> TryLeaveAsync()
    {
        if (ViewModel is { IsSending: true } or { IsPreparingSave: true } || HasDialog || transferLeaving)
            return false;
        if (transferEditor is not { } state)
            return true;
        SynchronizeInput();
        transferLeaving = true;
        try
        {
            if (ViewModel?.HasUnknownWrite == true)
            {
                if (await ConfirmTransferAsync("結果未確認の操作があります", "保存結果を確認できていません。離れてもサーバー側の処理は取り消されません。", "結果未確認のまま離れる", close: "結果の確認に戻る") != ContentDialogResult.Primary)
                    return false;
            }
            else if (state.NeedsLeaveConfirmation)
            {
                var writable = !TransferFrozen;
                var answer = await ConfirmTransferAsync("未保存の内容があります", writable ? "入力内容を保存して移動しますか？" : "入力内容を破棄して閉じますか？", writable ? "保存して移動" : "破棄して閉じる", writable ? "破棄して移動" : "", "編集に戻る");
                if (answer == ContentDialogResult.None)
                    return false;
                if (writable && answer == ContentDialogResult.Primary)
                    return await SaveTransferAsync(false);
            }

            return true;
        }
        finally { transferLeaving = false; }
    }
    public void SuspendForAuthentication()
    {
        SynchronizeInput();
        RememberPosition();
        ViewModel?.SuspendForAuthentication();
        confirmation?.Hide();
        Suspend();
    }
    public void MastersChanged()
    {
        if (ViewModel is not { Context: { } context } model)
            return;
        model.UpdateMasters(context.Masters.People(context.LedgerId), context.Masters.Payments(context.LedgerId));
        RefreshChoices();
    }
    public async Task<LedgerInfo?> ResumeAsync(CancellationToken token)
    {
        if (ViewModel is not { } model)
            return null;
        var ledger = await model.LoadRecoveryMastersAsync(token);
        if (!ReferenceEquals(ViewModel, model) || model.AwaitingLogin)
            return null;
        RefreshChoices();
        RestorePosition(() => ReferenceEquals(ViewModel, model) && !model.AwaitingLogin && IsLoaded && Visibility == Visibility.Visible);
        return ledger;
    }
    public void Release()
    {
        if (ViewModel is not { } model)
            return;
        ViewModel = null;
        model.ActionRequested -= ExecuteActionAsync;
        Bindings.StopTracking();
        model.Release();
        TransferName.Loaded -= ObserveNameInput;
        TransferName.UnregisterPropertyChangedCallback(AutoSuggestBox.IsSuggestionListOpenProperty, suggestionCallback);
        if (nameInput is { } input)
        {
            input.TextCompositionStarted -= NameCompositionStarted;
            input.TextCompositionEnded -= NameCompositionEnded;
            nameInput = null;
        }
        CancelRequests();
        confirmation?.Hide();
        Loaded -= InitialFocus;
        Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ReturnRequested = null;
        RetryMastersRequested = null;
        Saved = null;
        IsCancellationTarget = null;
        Discard();
    }
}
