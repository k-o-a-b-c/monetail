using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Presentation.Records;

namespace Monetail.Windows.Presentation.Transfers;

public sealed partial class TransferEditorViewModel
{
    private enum RecoveryPhase
    {
        None, Login, Loading, Failed
    }
    private RecoveryPhase recovery;
    private LedgerInfo? recoveryLedger;
    private CancellationTokenSource? recoveryLoading;
    private bool dialogOpen;
    public RecordEditorContext? Context
    {
        get; set;
    }
    public PaymentMethodsInfo? CurrentMethods => methods;
    public bool AwaitingLogin => recovery == RecoveryPhase.Login;
    public bool RecoveryBlocked => recovery != RecoveryPhase.None;
    public bool MastersLoading => recovery == RecoveryPhase.Loading;
    public bool MastersFailed => recovery == RecoveryPhase.Failed;
    private TransferEditorAvailability Availability
    {
        get
        {
            var external = availability();
            return new(RecoveryBlocked || external.RecoveryBlocked, MastersFailed || external.MastersFailed,
                MastersLoading || external.MastersLoading, dialogOpen || external.DialogOpen, Context is not null || external.HasAccount);
        }
    }
    public void SetDialogOpen(bool open)
    {
        dialogOpen = open;
        Refresh();
    }
    public void SuspendForAuthentication()
    {
        recovery = RecoveryPhase.Login;
        recoveryLedger = null;
        recoveryLoading?.Cancel();
        RememberAuthentication();
        Context?.Masters.Clear();
        Refresh();
    }
    public async Task<LedgerInfo?> LoadRecoveryMastersAsync(CancellationToken token)
    {
        if (Context is not { } context || recoveryLoading is not null)
            return null;
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
        recoveryLoading = loading;
        recovery = RecoveryPhase.Loading;
        bool Current() => !loading.IsCancellationRequested && ReferenceEquals(Context, context) && recovery == RecoveryPhase.Loading;
        context.Masters.Clear();
        Refresh();
        try
        {
            if (recoveryLedger is null)
            {
                var ledger = await new LedgerWorkspace(context.Api, context.Session).OpenAsync(context.LedgerId, loading.Token);
                if (!Current())
                    return null;
                recoveryLedger = ledger;
            }
            var currentPeople = await new PeopleWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            var currentMethods = await new PaymentMethodsWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            if (!Current())
                return null;
            context.Masters.UpdatePeople(context.LedgerId, currentPeople);
            context.Masters.UpdatePayments(context.LedgerId, currentMethods);
            var canEdit = TransferEditorRules.CanCreate(currentMethods) && (!Editor.IsExisting ||
                Editor.SavedFromPaymentMethodId is Guid from && Editor.SavedToPaymentMethodId is Guid to &&
                TransferEditorRules.CanUse(currentMethods, from) && TransferEditorRules.CanUse(currentMethods, to));
            if (!canEdit)
            {
                Editor.EnterReadOnly(Monetail.Windows.Client.Transfers.TransferReadOnlyReason.Forbidden);
                ShowNotice("編集権限がなくなったため、保存できません");
            }
            else if (Editor.ReadOnlyReason == Monetail.Windows.Client.Transfers.TransferReadOnlyReason.Conflict && !IsStopped)
                ShowNotice("他の端末で更新されています。入力を保持して読み取り専用にしました。編集を再開するには最新情報を読み込んでください。", TransferNoticeLevel.Warning);
            else if (!Editor.IsReadOnly && !IsStopped)
                ShowNotice(PendingRequest is not null ? IsUnknown ? "保存結果を確認できていません。同じ要求で再試行してください。" : "再ログインしました。保存処理は再開していません。同じ要求で再試行してください。" : "再ログインしました。入力内容を確認してください。", TransferNoticeLevel.Information);
            UpdateMasters(currentPeople, currentMethods);
            recovery = RecoveryPhase.None;
            return recoveryLedger with
            {
                Role = currentMethods.CurrentRole
            };
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                AuthenticationRequired?.Invoke();
        }
        catch (Exception error) when (error is LedgerRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound }
            or PeopleRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound }
            or PaymentMethodRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        {
            if (!Current())
                return null;
            var missing = error is LedgerRequestException { StatusCode: HttpStatusCode.NotFound } or PeopleRequestException { StatusCode: HttpStatusCode.NotFound } or PaymentMethodRequestException { StatusCode: HttpStatusCode.NotFound };
            Editor.EnterReadOnly(missing ? Monetail.Windows.Client.Transfers.TransferReadOnlyReason.NotFound : Monetail.Windows.Client.Transfers.TransferReadOnlyReason.Forbidden);
            ShowNotice(missing ? "振替が削除されたか、アクセスできなくなりました。" : "編集権限がなくなったため、保存できません");
            recovery = RecoveryPhase.None;
        }
        catch (Exception error) when (error is ApiConnectionException or LedgerRequestException or PeopleRequestException or PaymentMethodRequestException or HttpRequestException or IOException or OperationCanceledException)
        {
            if (Current())
            {
                recovery = RecoveryPhase.Failed;
                ShowNotice("マスタ情報を取得できませんでした");
            }
        }
        finally
        {
            if (ReferenceEquals(recoveryLoading, loading))
                recoveryLoading = null;
            Refresh();
        }
        return null;
    }
    public void Release()
    {
        recoveryLoading?.Cancel();
        Context = null;
        ActionRequested = null;
        AuthenticationRequired = null;
        DiscardWrite();
    }
}
