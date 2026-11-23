using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Records;

namespace Monetail.Windows.Presentation.Transfers;

public sealed partial class TransferEditorViewModel
{
    public static async Task<TransferEditorViewModel?> OpenAsync(RecordEditorContext context, Guid? id, CancellationToken token)
    {
        var ledger = context.LedgerId;
        var saved = id is Guid value ? await new TransfersWorkspace(context.Api, context.Session, ledger).GetAsync(value, token) : null;
        var people = await context.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(context.Api, context.Session, ledger).ListAsync(ct), token);
        var methods = await context.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(context.Api, context.Session, ledger).ListAsync(ct), token);
        token.ThrowIfCancellationRequested();
        if (saved is null && !TransferEditorRules.CanCreate(methods))
            return null;
        var editor = saved is null ? TransferEditorState.Create(DateOnly.FromDateTime(DateTime.Now)) : TransferEditorState.Edit(saved);
        if (saved is not null && (!TransferEditorRules.CanUse(methods, saved.FromPaymentMethodId) || !TransferEditorRules.CanUse(methods, saved.ToPaymentMethodId)))
            editor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
        var model = new TransferEditorViewModel(editor, static () => new(false, false, false, false, true),
            paymentId => context.Masters.PaymentName(ledger, paymentId),
            (query, ct) => new TransfersWorkspace(context.Api, context.Session, ledger).SuggestAsync(query, ct),
            (query, ct) => new TransfersWorkspace(context.Api, context.Session, ledger).AutofillAsync(query, ct))
        {
            Context = context
        };
        model.UpdateMasters(people, methods);
        return model;
    }
}
