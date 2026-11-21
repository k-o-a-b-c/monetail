using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    // 入力値は元のモデル/コントロールに残す。ここには復帰先だけを保持し、フォーカスと表示位置はViewへ任せる。
    private sealed record TransferAuthentication(Guid AccountId, Guid LedgerId, string ApiOrigin);
    private TransferAuthentication? transferAuthentication;
    private bool transferAwaitingLogin, transferMastersLoading, transferMastersFailed, transferRecoveryLedgerReady;

    // 表示切替と分けて退避を決める。入力/要求は既存オブジェクトに保持する。
    private bool SuspendTransferForAuthentication()
    {
        if (transferEditor is not null && account is not null) RememberTransferAuthentication();
        if (transferAuthentication is not { } pending || pending.ApiOrigin != connection.Endpoint?.Value) return false;
        transferAwaitingLogin = true;
        return true;
    }

    private void RememberTransferAuthentication()
    {
        if (transferEditor is null || account is null || currentLedger is null || connection.Endpoint is null) return;
        SyncTransferInput();
        // マスタ再取得中の連続401では、最初の編集位置をログイン画面付近の位置で上書きしない。
        if (transferAuthentication is not { } context || context.AccountId != account.AuthUserId ||
            context.LedgerId != currentLedger.LedgerId || context.ApiOrigin != connection.Endpoint.Value)
        {
            TransferEntry.RememberPosition();
            transferAuthentication = new(account.AuthUserId, currentLedger.LedgerId, connection.Endpoint.Value);
        }
        transferAwaitingLogin = true; transferRecoveryLedgerReady = false; transferMastersFailed = false;
        TransferEntry.ViewModel?.RememberAuthentication();
        TransferEntry.Suspend();
        ReceiptListModel?.Suspend();
        TransferListModel?.Suspend();
        connection.Masters.Clear();
        RenderTransferEditor();
    }

    private void DiscardTransferAuthentication() => ResetTransferEditor();

    private async Task<bool> ResumeTransferEditAsync(CancellationToken token)
    {
        if (transferAuthentication is not { } context || !transferAwaitingLogin || account is null) return false;
        if (context.AccountId != account.AuthUserId || context.ApiOrigin != connection.Endpoint?.Value)
        {
            DiscardLedgerContents(); ClearLedger(); return false;
        }
        transferAwaitingLogin = false; transferMastersLoading = true;
        if (currentLedger is { } ledger) DisplayLedgerHeader(ledger);
        NavigateWorkspace(WorkspaceScreen.HomePage);
        RenderTransferEditor();
        await LoadTransferRecoveryMastersAsync(context, token);
        return true;
    }

    private async Task RetryTransferMastersAsync()
    {
        if (transferMastersFailed && !transferMastersLoading && transferAuthentication is { } context)
            await LoadTransferRecoveryMastersAsync(context, lifetime.Token);
    }

    private async Task LoadTransferRecoveryMastersAsync(TransferAuthentication context, CancellationToken token)
    {
        if (transferEditor is not { } editor) return;
        bool Current() => !closed && ReferenceEquals(transferAuthentication, context) && ReferenceEquals(editor, transferEditor) &&
            !transferAwaitingLogin && account?.AuthUserId == context.AccountId && connection.Endpoint?.Value == context.ApiOrigin && currentLedger?.LedgerId == context.LedgerId;
        if (!Current()) return;
        transferMastersLoading = true; transferMastersFailed = false; connection.Masters.Clear(); RenderTransferEditor();
        try
        {
            if (!transferRecoveryLedgerReady)
            {
                var ledger = await connection.Ledgers.OpenAsync(context.LedgerId, token);
                if (!Current()) return;
                DisplayLedgerHeader(ledger); transferRecoveryLedgerReady = true;
            }
            var people = await new PeopleWorkspace(connection.Api, connection.Session, context.LedgerId).ListAsync(token);
            var methods = await new PaymentMethodsWorkspace(connection.Api, connection.Session, context.LedgerId).ListAsync(token);
            if (!Current()) return;
            connection.Masters.UpdatePeople(context.LedgerId, people); connection.Masters.UpdatePayments(context.LedgerId, methods);
            if (currentLedger is { } ledgerInfo) DisplayLedgerHeader(ledgerInfo with { Role = methods.CurrentRole });
            peoplePermissions = new(methods.CurrentRole, methods.MyPersonId);
            var canEdit = TransferEditorRules.CanCreate(methods) && (!editor.IsExisting ||
                editor.SavedFromPaymentMethodId is Guid from && editor.SavedToPaymentMethodId is Guid to && TransferEditorRules.CanUse(methods, from) && TransferEditorRules.CanUse(methods, to));
            if (!canEdit) { editor.EnterReadOnly(TransferReadOnlyReason.Forbidden); TransferNotice("編集権限がなくなったため、保存できません"); }
            else if (editor.ReadOnlyReason == TransferReadOnlyReason.Conflict && TransferEntry.ViewModel?.IsStopped == false)
                TransferNotice("他の端末で更新されています。入力を保持して読み取り専用にしました。編集を再開するには最新情報を読み込んでください。", InfoBarSeverity.Warning);
            else if (!editor.IsReadOnly && TransferEntry.ViewModel?.IsStopped == false)
                TransferNotice(TransferEntry.ViewModel?.PendingRequest is not null ? TransferEntry.ViewModel.IsUnknown ? "保存結果を確認できていません。同じ要求で再試行してください。" : "再ログインしました。保存処理は再開していません。同じ要求で再試行してください。" : "再ログインしました。入力内容を確認してください。", InfoBarSeverity.Informational);
            transferAuthentication = null;
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。"); }
        catch (Exception error) when (error is LedgerRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound }
            or PeopleRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound }
            or PaymentMethodRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        {
            if (!Current()) return;
            var missing = error is LedgerRequestException { StatusCode: HttpStatusCode.NotFound } or PeopleRequestException { StatusCode: HttpStatusCode.NotFound } or PaymentMethodRequestException { StatusCode: HttpStatusCode.NotFound };
            editor.EnterReadOnly(missing ? TransferReadOnlyReason.NotFound : TransferReadOnlyReason.Forbidden);
            TransferNotice(missing ? "振替が削除されたか、アクセスできなくなりました。" : "編集権限がなくなったため、保存できません");
            transferAuthentication = null;
        }
        catch (Exception error) when (error is ApiConnectionException or LedgerRequestException or PeopleRequestException or PaymentMethodRequestException or HttpRequestException or IOException or OperationCanceledException)
        { if (Current()) { transferMastersFailed = true; TransferNotice("マスタ情報を取得できませんでした"); } }
        finally
        {
            if (!closed && ReferenceEquals(editor, transferEditor) && !transferAwaitingLogin && (transferAuthentication is null || ReferenceEquals(transferAuthentication, context)))
            {
                transferMastersLoading = false; RefreshTransferEditorChoices(); RenderTransferEditor(); RestoreTransferRecoveryView(context);
            }
        }
    }

    private void RestoreTransferRecoveryView(TransferAuthentication context)
    {
        var editor = transferEditor;
        TransferEntry.RestorePosition(() => !closed && !transferAwaitingLogin && editor is not null &&
            ReferenceEquals(editor, transferEditor) && IsRecordPageVisible &&
            account?.AuthUserId == context.AccountId && connection.Endpoint?.Value == context.ApiOrigin &&
            currentLedger?.LedgerId == context.LedgerId);
    }
}
