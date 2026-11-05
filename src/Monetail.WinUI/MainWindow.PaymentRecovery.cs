using System.Net;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private PaymentEditSnapshot? pendingPaymentEdit;
    private sealed record PaymentEditSnapshot(Guid AccountId, string ApiOrigin, LedgerInfo Ledger, PeopleInfo People,
        PaymentMethodsInfo Payments, PaymentMethodInfo? Row, PaymentMethodCreationDraft? Draft, Guid? Owner,
        string Name, string From, string To, bool IncludeInactive, Guid OrderOwner, Guid OrderRevision, PaymentMethodInfo[]? Order);
    private void RememberPaymentEdit()
    {
        if (account is null || currentLedger is null || paymentInfo is null || paymentPeople is null ||
            (orderedPayments is null && (!paymentDialogOpen || deletingPayment))) return;
        pendingPaymentEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, paymentPeople, paymentInfo,
            editingPayment, paymentDraft, ChosenPaymentOwner, PaymentName.Text, PaymentFrom.Text, PaymentTo.Text,
            IncludeInactivePayments.IsChecked == true, paymentOrderOwner, paymentOrderRevision, orderedPayments?.ToArray());
    }
    private void DropPendingPaymentEdit() => pendingPaymentEdit = null;
    private async Task<bool> ResumePaymentEditAsync(CancellationToken token)
    {
        if (pendingPaymentEdit is not { } draft || account is null) return false;
        if (draft.AccountId != account.AuthUserId || draft.ApiOrigin != connection.Endpoint!.Value) { DropPendingPaymentEdit(); return false; }
        await paymentDialogTask;
        var ledger = draft.Ledger; var people = draft.People; var payments = draft.Payments;
        PeoplePermissions? denied = null; string? failure = null;
        try
        {
            ledger = await connection.Ledgers.OpenAsync(draft.Ledger.LedgerId, token);
            people = await new PeopleWorkspace(connection.Api, connection.Session, ledger.LedgerId).ListAsync(token);
            payments = await new PaymentMethodsWorkspace(connection.Api, connection.Session, ledger.LedgerId).ListAsync(token);
        }
        catch (LedgerRequestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { denied = new(null, null); failure = "この家計簿を開けません。未保存の入力を残していますが保存できません。"; }
        catch (PeopleRequestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { denied = e.Permissions ?? new(null, null); failure = "現在の権限では操作できません。未保存の入力は残しています。"; }
        catch (PaymentMethodRequestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { denied = e.Permissions ?? new(null, null); failure = "現在の権限では操作できません。未保存の入力は残しています。"; }
        DisplayLedger(ledger); paymentPeople = people; DisplayPayments(payments);
        if (denied is not null) paymentPermissions = denied;
        ShowPage(ManagementPage); ManagementNavigation.SelectedItem = PaymentsNavigation;
        PeopleContent.Visibility = ParticipantsContent.Visibility = InvitationsContent.Visibility = AccountContent.Visibility = ConnectionSettingsContent.Visibility = Visibility.Collapsed;
        PaymentsContent.Visibility = Visibility.Visible; IncludeInactivePayments.IsChecked = draft.IncludeInactive;
        if (draft.Order is { } order)
        {
            var current = payments.PaymentMethods.ToDictionary(r => r.PaymentMethodId);
            BeginPaymentOrder(draft.OrderOwner, draft.OrderRevision, order.Select(r => current.GetValueOrDefault(r.PaymentMethodId) ?? r));
            paymentOrderDenied = failure is not null || !CanManagePaymentOwner(draft.OrderOwner);
            ShowPaymentOrderNotice(failure ?? (paymentOrderDenied ? "現在の権限では保存できません。未保存の並び順は残しています。" : "未保存の並び順を復元しました。内容を確認してください。"), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        }
        else
        {
            OpenPaymentEditor(draft.Row, owner: draft.Owner);
            PaymentName.Text = draft.Name; PaymentFrom.Text = draft.From; PaymentTo.Text = draft.To; paymentDraft = draft.Draft;
            if (draft.Row is not null && !payments.PaymentMethods.Any(r => r.PaymentMethodId == draft.Row.PaymentMethodId)) missingPayments.Add(draft.Row.PaymentMethodId);
            paymentEditorDenied = failure is not null || (draft.Row is null ? draft.Owner is null || !CanManagePaymentOwner(draft.Owner.Value) : !CanChangePayment(draft.Row));
            PaymentRestoredNotice.Visibility = Visibility.Visible;
            if (paymentEditorDenied) ShowPaymentError(null, failure ?? "現在の権限では保存できません。未保存の入力は残しています。");
        }
        pendingPaymentEdit = null; RenderPayments(); UpdatePaymentActions(); return true;
    }
}
