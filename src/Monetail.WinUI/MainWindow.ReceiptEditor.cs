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
    private ReceiptEditorState? receiptEditor => ReceiptPage?.ViewModel?.Editor;
    private Guid? receiptOpenRetryId;
    private CancellationTokenSource? receiptOpening;


    // 一要求だけを受理。取消後に通信が完了しても同一要求・本人・家計簿・画面を照合して適用しない。
    internal async Task OpenReceiptEditorAsync(Guid? receiptId)
    {
        if (busy || closed || ManagementDialogOpen || receiptOpening is not null || transferOpening is not null || RecordEditorActive || account is null || currentLedger is null || !IsRecordPageVisible)
            return;
        var ledger = currentLedger.LedgerId;
        var person = account.AuthUserId;
        var api = connection.Api;
        var session = connection.Session;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiptOpening = pending;
        receiptOpenRetryId = receiptId;

        UpdateReceiptActions();
        if (recordHome is not null)
            recordHome.ReceiptOpening = new("レシートを読み込み中…", true, false);
        bool Current() => !closed && ReferenceEquals(receiptOpening, pending) && !pending.IsCancellationRequested &&
            currentLedger?.LedgerId == ledger && account?.AuthUserId == person && IsRecordPageVisible;
        try
        {
            var model = await ReceiptEditorViewModel.OpenAsync(CreateRecordEditorContext(), receiptId, pending.Token);
            if (!Current())
            {
                model.Discard();
                return;
            }
            model.ActionRequested += action => action switch
            {
                ReceiptEditorAction.Back => BackFromReceiptEditorAsync(),
                ReceiptEditorAction.RetryMasters => RetryReceiptMastersAsync(),
                _ => ReportReceiptCategoryErrorAsync()
            };
            model.AuthenticationRequired += StopReceiptAuthentication;
            NavigateReceiptEditor(model);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                NavigateToLogin("ログインを継続できません。もう一度ログインしてください。");
        }
        catch (Exception error) when (error is TaxRateRequestException or ReceiptRequestException or CategoryRequestException or PeopleRequestException or PaymentMethodRequestException or ApiConnectionException or HttpRequestException or OperationCanceledException)
        {
            if (Current())
            {
                if (recordHome is not null)
                    recordHome.ReceiptOpening = new(error is ReceiptRequestException { StatusCode: HttpStatusCode.NotFound }
                ? "このレシートは開けません。参加状態と対象を確認してください。"
                : "レシートまたは税率履歴を取得できませんでした。再試行してください。", false, true);
            }
        }
        finally
        {
            if (ReferenceEquals(receiptOpening, pending))
            {
                receiptOpening = null;
                if (recordHome is not null && receiptEditor is not null)
                    recordHome.ReceiptOpening = null;
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
        CancelReceiptOpening();
        ReceiptPage?.Release();
    }
    private void RefreshReceiptEditorChoices()
    {
        if (ReceiptPage is { ViewModel.IsReadOnly: false } page)
            page.RefreshMasters(false);
    }
    private Task ReportReceiptCategoryErrorAsync()
    {
        ShowError("科目情報を確認できないため切り替えできません。入力は保持しています。");
        return Task.CompletedTask;
    }
    private void StopReceiptAuthentication() => NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。");
    private async Task BackFromReceiptEditorAsync()
    {
        if (await LeaveReceiptEditorAsync())
            NavigateWorkspace(WorkspaceScreen.HomePage);
    }
    private async Task<bool> LeaveReceiptEditorAsync()
    {
        if (ReceiptPage is { } page)
        {
            if (!await page.TryLeaveAsync())
                return false;
            page.Release();
            return true;
        }
        CancelReceiptOpening();
        return true;
    }
}
