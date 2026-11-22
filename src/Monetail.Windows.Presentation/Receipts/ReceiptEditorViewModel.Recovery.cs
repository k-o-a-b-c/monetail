using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Records;

namespace Monetail.Windows.Presentation.Receipts;

public sealed partial class ReceiptEditorViewModel
{
    private enum RecoveryPhase
    {
        None, Login, Loading, Failed, Forbidden, Unavailable
    }
    private RecoveryPhase recovery;
    private CancellationTokenSource? recoveryLoading;
    private bool dialogOpen;
    public RecordEditorContext? Context
    {
        get; set;
    }
    public bool AwaitingLogin => recovery == RecoveryPhase.Login;
    public event Action? AuthenticationRequired;
    public void SetDialogOpen(bool value)
    {
        dialogOpen = value;
        Refresh();
    }
    public void SuspendForAuthentication()
    {
        recovery = RecoveryPhase.Login;
        recoveryLoading?.Cancel();
        Context?.Masters.Clear();
        Context?.Session.TaxRates.Clear();
        TaxHistory = null;
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
        context.Session.TaxRates.Clear();
        TaxHistory = null;
        Refresh();
        try
        {
            var ledger = await new LedgerWorkspace(context.Api, context.Session).OpenAsync(context.LedgerId, loading.Token);
            if (!Current())
                return null;
            var people = await new PeopleWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            if (!Current())
                return null;
            var categories = await new CategoriesWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            if (!Current())
                return null;
            var taxes = await new TaxRatesWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            if (!Current())
                return null;
            var payments = await new PaymentMethodsWorkspace(context.Api, context.Session, context.LedgerId).ListAsync(loading.Token);
            if (!Current())
                return null;
            context.Masters.UpdatePeople(context.LedgerId, people);
            context.Masters.UpdatePayments(context.LedgerId, payments);
            context.Masters.UpdateCategories(context.LedgerId, categories);
            TaxHistory = taxes;
            var canEdit = payments.CurrentRole == "Admin" || payments.CurrentRole == "Editor" && payments.MyPersonId is Guid person &&
                (Editor.ReceiptId is null || payments.PaymentMethods.Any(p => p.PaymentMethodId == Editor.SavedPaymentMethodId && p.OwnerPersonId == person));
            recovery = canEdit ? RecoveryPhase.None : RecoveryPhase.Forbidden;
            return ledger with
            {
                Role = payments.CurrentRole
            };
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            if (Current())
                AuthenticationRequired?.Invoke();
        }
        catch (Exception error) when (error is LedgerRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            PeopleRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            PaymentMethodRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            CategoryRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            TaxRateRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        {
            if (Current())
                recovery = RecoveryPhase.Unavailable;
        }
        catch (Exception error) when (error is LedgerRequestException or PeopleRequestException or PaymentMethodRequestException or
            CategoryRequestException or TaxRateRequestException or ApiConnectionException or HttpRequestException or IOException or OperationCanceledException)
        {
            if (Current())
                recovery = RecoveryPhase.Failed;
        }
        finally
        {
            if (ReferenceEquals(recoveryLoading, loading))
                recoveryLoading = null;
            Refresh();
        }
        return null;
    }
}
