using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;
using Monetail.Windows.Presentation.Transfers;

namespace Monetail.Windows.Presentation.Workspace;
public sealed record RecordFilterChoice(Guid? Id, string Name);
public sealed record RecordOpeningNotice(string Message, bool Loading, bool CanRetry);
/// <summary>共通検索の正本と既存一覧VMを所有する。View/Windowや業務入力を複製しない。</summary>
public sealed partial class RecordHomeViewModel(RecordSearchState search, ReceiptListViewModel receipts, TransferListViewModel transfers) : ObservableObject, IDisposable
{
    public RecordSearchState Search => search;
    public ReceiptListViewModel Receipts => receipts;
    public TransferListViewModel Transfers => transfers;

    private bool disposed;
    private IReadOnlyList<RecordFilterChoice> owners = [], payments = [];
    public IReadOnlyList<RecordFilterChoice> Owners => owners;
    public IReadOnlyList<RecordFilterChoice> Payments => payments;
    public RecordFilterChoice? Owner => owners.FirstOrDefault(p => p.Id == search.Receipts.OwnerPersonId);
    public RecordFilterChoice? Payment => payments.FirstOrDefault(p => p.Id == search.Receipts.PaymentMethodId);

    public string MonthText
    {
        get => search.Receipts.MonthText;
        set
        {
            if (MonthText != value)
            {
                search.Receipts.MonthText = value;
                OnPropertyChanged();
            }
        }
    }

    public string Keyword
    {
        get => search.TransferText;
        set
        {
            if (Keyword != value)
            {
                search.TransferText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AllPeriods => search.Receipts.AllPeriods;
    public string? MonthError => search.Receipts.MonthError;
    public string AppliedMonth => "適用中：" + (AllPeriods ? "全期間" : search.Receipts.AppliedMonth);
    public bool CanMovePrevious => search.Receipts.CanMove(-1);
    public bool CanMoveNext => search.Receipts.CanMove(1);

    private RecordOpeningNotice? receiptOpening, transferOpening;
    public RecordOpeningNotice? ReceiptOpening { get => receiptOpening; set => SetProperty(ref receiptOpening, value); }
    public RecordOpeningNotice? TransferOpening { get => transferOpening; set => SetProperty(ref transferOpening, value); }

    public event Func<Task>? RetryReceiptRequested;
    public event Func<Task>? RetryTransferRequested;
    [RelayCommand]
    private Task RetryReceiptAsync() => RetryReceiptRequested?.Invoke() ?? Task.CompletedTask;
    [RelayCommand]
    private Task RetryTransferAsync() => RetryTransferRequested?.Invoke() ?? Task.CompletedTask;
    public Task StartAsync(CancellationToken token = default)
    {
        if (disposed)
            return Task.CompletedTask;
        var first = receipts.StartAsync(search.Receipts.Query(), token);
        return Task.WhenAll(first, disposed ? Task.CompletedTask : transfers.StartAsync(search.TransferQuery(), token));
    }

    public Task ResumeAsync(CancellationToken token = default) => disposed ? Task.CompletedTask : !receipts.HasStarted ? StartAsync(token) : Task.WhenAll(receipts.ResumeAsync(token), transfers.ResumeAsync(token));
    public void Suspend()
    {
        receipts.Suspend();
        transfers.Suspend();
    }

    public void RefreshChoices(PeopleInfo? people, PaymentMethodsInfo? methods, Guid? mine)
    {
        var persons = people?.People ?? [];
        owners = new[]
        {
            new RecordFilterChoice(null, "全員")
        }.Concat(persons.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder).Select(p => new RecordFilterChoice(p.PersonId, p.Name))).ToArray();
        var order = persons.OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder).Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        payments = new[]
        {
            new RecordFilterChoice(null, "全件")
        }.Concat((methods?.PaymentMethods ?? []).OrderBy(p => order.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder).Select(p => new RecordFilterChoice(p.PaymentMethodId, receipts.PaymentDisplayName(p.PaymentMethodId)))).ToArray();
        OnPropertyChanged(nameof(Owners));
        OnPropertyChanged(nameof(Payments));
        RefreshSearch();
    }

    public void RefreshSearch()
    {
        foreach (var name in new[]
        {
            nameof(MonthText),
            nameof(Keyword),
            nameof(AllPeriods),
            nameof(MonthError),
            nameof(AppliedMonth),
            nameof(CanMovePrevious),
            nameof(CanMoveNext),
            nameof(Owner),
            nameof(Payment)
        }

        )
            OnPropertyChanged(name);
    }

    public Task ReloadAsync(CancellationToken token = default)
    {
        RefreshSearch();
        return StartAsync(token);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ApplyMonthAsync()
    {
        var valid = search.Receipts.ApplyMonthText();
        RefreshSearch();
        return valid ? StartAsync() : Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ApplyKeywordAsync()
    {
        var valid = search.ApplySharedText(Keyword);
        RefreshSearch();
        return valid ? StartAsync() : Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MoveMonthAsync(int delta)
    {
        if (!search.Receipts.MoveMonth(delta))
            return Task.CompletedTask;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetPeriodAsync(bool all)
    {
        search.Receipts.AllPeriods = all;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetOwnerAsync(RecordFilterChoice choice)
    {
        search.Receipts.OwnerPersonId = choice.Id;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetPaymentAsync(RecordFilterChoice choice)
    {
        search.Receipts.PaymentMethodId = choice.Id;
        return ReloadAsync();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        receipts.Dispose();
        transfers.Dispose();
        RetryReceiptRequested = null;
        RetryTransferRequested = null;
    }
}
