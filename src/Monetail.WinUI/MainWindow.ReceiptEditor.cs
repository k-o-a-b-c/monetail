using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Receipts;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private ReceiptEditorState? receiptEditor;
    private Guid? receiptOpenRetryId;
    private CancellationTokenSource? receiptOpening;
    private bool receiptDialogOpen, receiptLeaving, receiptCloseApproved;

    private void InitializeReceiptEditor()
    {
        WorkspaceRoot.Children.Remove(ReceiptConfirmationDialog);
        ReceiptEntry.IsCancellationTarget = IsReceiptLeaveTarget;
        AppWindow.Closing += async (_, args) =>
        {
            if (receiptRecoveryPhase == ReceiptRecoveryPhase.Login) { DiscardReceiptAuthentication(); return; }
            if (receiptCloseApproved || receiptEditor is null) return;
            args.Cancel = true;
            if (await LeaveReceiptEditorAsync()) { receiptCloseApproved = true; Close(); }
        };
    }
    private bool IsReceiptLeaveTarget(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, SettingsButton) ||
                ReferenceEquals(element, RecordButton) || ReferenceEquals(element, LedgerSelector)) return true;
        return false;
    }
    // 一要求だけを受理。取消後に通信が完了しても同一要求・本人・家計簿・画面を照合して適用しない。
    internal async Task OpenReceiptEditorAsync(Guid? receiptId)
    {
        if (busy || closed || ManagementDialogOpen || receiptOpening is not null || transferOpening is not null || RecordEditorActive || account is null || currentLedger is null || !IsRecordPageVisible) return;
        var ledger = currentLedger.LedgerId; var person = account.AuthUserId;
        var api = connection.Api; var session = connection.Session;
        var initial = new ReceiptHeaderDraft { TransactionAt = DateTime.Today, ShopLocation = "", Memo = null, TaxInclusionMode = 0, TaxRoundingMode = "floor" };
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiptOpening = pending;
        receiptOpenRetryId = receiptId;

        UpdateReceiptActions();
        if (recordHome is not null) recordHome.ReceiptOpening = new("レシートを読み込み中…", true, false);
        bool Current() => !closed && ReferenceEquals(receiptOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person && IsRecordPageVisible;
        try
        {
            var info = receiptId is Guid id ? await new ReceiptsWorkspace(api, session, ledger).GetAsync(id, pending.Token) : null;
            await connection.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            await connection.Masters.GetCategoriesAsync(ledger, ct => new CategoriesWorkspace(api, session, ledger).ListAsync(ct), pending.Token);
            if (!Current()) return;
            var taxes = await new TaxRatesWorkspace(api, session, ledger).ListAsync(pending.Token);
            if (!Current()) return;
            ReceiptCategoryDefaults Defaults(Guid category, DateTime? date)
            {
                var master = connection.Masters.Categories(ledger)?.Categories.FirstOrDefault(c => c.CategoryId == category)
                    ?? throw new ReceiptInputException("categoryId", "科目情報を取得できませんでした。");
                // 未確定の生文字列がある間は、最後の有効なモデル日付で初期値を代用しない。
                return new(master.IncomeExpense, ReceiptEntry.ViewModel?.TaxHistory is { } currentTaxes ? ReceiptTaxRules.Initial(currentTaxes, master.TaxCategoryCode, ReceiptEntry.ConfirmedDay) : ReceiptTaxSelection.Unselected);
            }
            receiptEditor = info is null ? ReceiptEditorState.Create(initial, Defaults) : ReceiptEditorState.Edit(info, Defaults);
            var model = new ReceiptEditorViewModel(receiptEditor, () => new(ReceiptRecoveryReadOnly,
                ReceiptRecoveryMessage, receiptRecoveryPhase == ReceiptRecoveryPhase.Failed, receiptDialogOpen),
                () => (connection.Masters.People(ledger), connection.Masters.Payments(ledger), connection.Masters.Categories(ledger), connection.Masters.MyPersonId(ledger)),
                paymentId => connection.Masters.PaymentName(ledger, paymentId)) { TaxHistory = taxes };
            model.ActionRequested += action => action switch
            {
                ReceiptEditorAction.Back => BackFromReceiptEditorAsync(),
                ReceiptEditorAction.RetryMasters => RetryReceiptMastersAsync(),
                _ => ReportReceiptCategoryErrorAsync()
            };
            model.ConfirmationRequested += kind => ConfirmReceiptEditorAsync(model, kind);
            ReceiptEntry.ViewModel = model;
            RecordPage?.Suspend();
            RecordPage?.RememberPositions();
            ReceiptListModel?.Suspend();
            TransferListModel?.Suspend();
            RecordPage?.ShowEditor(EditorContents);
            ReceiptEditorPage.Visibility = Visibility.Visible;
            PopulateReceiptEditor();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) NavigateToLogin("ログインを継続できません。もう一度ログインしてください。"); }
        catch (Exception error) when (error is TaxRateRequestException or ReceiptRequestException or CategoryRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                if (recordHome is not null) recordHome.ReceiptOpening = new(error is ReceiptRequestException { StatusCode: HttpStatusCode.NotFound }
                ? "このレシートは開けません。参加状態と対象を確認してください。"
                : "レシートまたは税率履歴を取得できませんでした。再試行してください。", false, true);
            }
        }
        finally
        {
            if (ReferenceEquals(receiptOpening, pending))
            {
                receiptOpening = null;
                if (recordHome is not null && receiptEditor is not null) recordHome.ReceiptOpening = null;
                UpdateReceiptActions();
            }
        }
    }
    private void CancelReceiptOpening()
    {
        var pending = receiptOpening; receiptOpening = null; pending?.Cancel();
        if (recordHome is not null) recordHome.ReceiptOpening = null;
        UpdateReceiptActions();
    }
    private void ResetReceiptEditor()
    {
        receiptAuthentication = null; receiptRecoveryPhase = ReceiptRecoveryPhase.None; receiptRecoveryLoading?.Cancel(); receiptRecoveryLoading = null;
        CancelReceiptOpening(); receiptEditor = null;
        var model = ReceiptEntry.ViewModel;
        ReceiptEntry.ViewModel = null;
        model?.Discard();
        ReceiptEditorPage.Visibility = Visibility.Collapsed; RecordPage?.ShowEditor(null);
    }
    private void PopulateReceiptEditor() => ReceiptEntry.Populate();
    private void RefreshReceiptEditorChoices()
    {
        if (receiptEditor is not null && currentLedger is not null && !ReceiptRecoveryReadOnly)
            ReceiptEntry.RefreshMasters(receiptAuthentication is not null);
    }
    private Task ReportReceiptCategoryErrorAsync()
    {
        ShowError("科目情報を確認できないため切り替えできません。入力は保持しています。");
        return Task.CompletedTask;
    }
    private async Task<ReceiptDraftChoice> ConfirmReceiptEditorAsync(ReceiptEditorViewModel model, ReceiptEditorConfirmation kind)
    {
        if (receiptDialogOpen || !ReferenceEquals(model, ReceiptEntry.ViewModel)) return ReceiptDraftChoice.Stay;
        var dialog = ReceiptConfirmationDialog;
        dialog.XamlRoot = WorkspaceRoot.XamlRoot;
        if (kind == ReceiptEditorConfirmation.UnappliedDetail)
        {
            dialog.Title = "未反映の明細があります";
            dialog.Content = "現在の明細入力をどうしますか？";
            dialog.PrimaryButtonText = model.Editor.SelectedDetailKey is null ? "追加して移動" : "更新して移動";
            dialog.SecondaryButtonText = "入力を破棄して移動";
            dialog.CloseButtonText = "編集に戻る";
        }
        else
        {
            var group = model.Editor.SelectedGroup;
            dialog.Title = "科目を削除";
            dialog.Content = $"「{model.SelectedGroup?.Name}」と明細{group?.Details.Count}件を削除しますか？";
            dialog.PrimaryButtonText = "削除";
            dialog.SecondaryButtonText = "";
            dialog.CloseButtonText = "キャンセル";
        }
        receiptDialogOpen = true;
        try
        {
            var choice = await dialog.ShowAsync();
            if (closed || ReceiptRecoveryReadOnly || !ReferenceEquals(model, ReceiptEntry.ViewModel)) return ReceiptDraftChoice.Stay;
            return choice switch
            {
                ContentDialogResult.Primary => ReceiptDraftChoice.ApplyAndMove,
                ContentDialogResult.Secondary => ReceiptDraftChoice.DiscardAndMove,
                _ => ReceiptDraftChoice.Stay
            };
        }
        finally { receiptDialogOpen = false; dialog.SecondaryButtonText = ""; }
    }
    private async Task BackFromReceiptEditorAsync()
    {
        if (!await LeaveReceiptEditorAsync()) return;
        NavigateWorkspace(WorkspaceScreen.HomePage); RestoreReceiptListPosition();
    }
    private async Task<bool> LeaveReceiptEditorAsync()
    {
        if (receiptLeaving || receiptDialogOpen || ReceiptEntry.ViewModel?.IsTransitioning == true) return false;
        if (receiptEditor is null) { CancelReceiptOpening(); return true; }
        // TextChangedの通知を待たず、離脱直前のコントロール値を必ず保護対象にする。
        ReceiptEntry.Synchronize();
        receiptLeaving = true;
        // ダイアログの既定ボタンへのフォーカス移動も取消操作の一部であり、日時を補正しない。
        ReceiptEntry.SuppressCompletion(true);
        try
        {
            if (ReceiptEntry.NeedsLeaveConfirmation)
            {
                var dialog = ReceiptConfirmationDialog; dialog.XamlRoot = WorkspaceRoot.XamlRoot;
                dialog.Title = "未保存の内容があります"; dialog.Content = "保存はまだ利用できません。入力内容を破棄して移動しますか？";
                dialog.SecondaryButtonText = ""; dialog.PrimaryButtonText = ReceiptRecoveryReadOnly ? "破棄して閉じる" : "破棄して移動"; dialog.CloseButtonText = "編集に戻る";
                receiptDialogOpen = true;
                try { if (await dialog.ShowAsync() != ContentDialogResult.Primary || receiptRecoveryPhase == ReceiptRecoveryPhase.Login) return false; }
                finally { receiptDialogOpen = false; }
            }
            ResetReceiptEditor(); return true;
        }
        finally { ReceiptEntry.SuppressCompletion(false); receiptLeaving = false; }
    }
    private void RestoreReceiptListPosition() => RecordPage?.RestorePositions();
}
