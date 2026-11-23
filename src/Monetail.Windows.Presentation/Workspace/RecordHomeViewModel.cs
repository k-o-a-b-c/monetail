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
public sealed partial class RecordHomeViewModel : ObservableObject, IDisposable
{
    public RecordSearchState Search
    {
        get;
    }
    public ReceiptListViewModel Receipts
    {
        get;
    }
    public TransferListViewModel Transfers
    {
        get;
    }

    private readonly Guid ledgerId;
    private readonly LedgerMasterCache? masters;
    private (MonetailApiClient Api, ClientSession Session) client;
    private CancellationTokenSource? masterLifetime;

    public RecordHomeViewModel(RecordSearchState search, ReceiptListViewModel receipts, TransferListViewModel transfers)
    {
        Search = search;
        Receipts = receipts;
        Transfers = transfers;
    }

    public RecordHomeViewModel(Guid ledgerId, DateOnly today, LedgerMasterCache masters,
        MonetailApiClient api, ClientSession session, CancellationToken token = default)
    {
        this.ledgerId = ledgerId;
        this.masters = masters;
        client = (api, session);
        masterLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Search = new(today);
        Receipts = new(async (query, cancellation) =>
        {
            var current = await GetMastersAsync(cancellation);
            return await new ReceiptsWorkspace(current.Api, current.Session, ledgerId).ListAsync(query, cancellation);
        }, Search, id => masters.PaymentName(ledgerId, id));
        Transfers = new(async (query, cancellation) =>
        {
            var current = await GetMastersAsync(cancellation);
            return await new TransfersWorkspace(current.Api, current.Session, ledgerId).ListAsync(query, cancellation);
        }, Search, id => masters.PaymentName(ledgerId, id));
        MastersChanged(ledgerId);
    }

    // 家計簿/本人/接続先の一致はShellが確認する。保持した一覧を再構成せず通信先だけを更新する。
    public void Rebind(MonetailApiClient api, ClientSession session, CancellationToken token)
    {
        if (disposed || masters is null || client == (api, session))
            return;
        Suspend();
        masterLifetime?.Cancel();
        masterLifetime?.Dispose();
        masterLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        client = (api, session);
    }

    private async Task<(MonetailApiClient Api, ClientSession Session)> GetMastersAsync(CancellationToken token)
    {
        var current = client;
        var cache = masters ?? throw new InvalidOperationException();
        var shared = masterLifetime?.Token ?? new CancellationToken(true);
        token.ThrowIfCancellationRequested();
        // 一覧片方の中断は待機だけを取り消し、もう片方も使うマスタ取得を中断しない。
        await cache.GetPeopleAsync(ledgerId, ct => new PeopleWorkspace(current.Api, current.Session, ledgerId).ListAsync(ct), shared).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        shared.ThrowIfCancellationRequested();
        await cache.GetPaymentsAsync(ledgerId, ct => new PaymentMethodsWorkspace(current.Api, current.Session, ledgerId).ListAsync(ct), shared).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        shared.ThrowIfCancellationRequested();
        return current;
    }

    public void MastersChanged(Guid ledger)
    {
        if (disposed || masters is null || ledger != ledgerId)
            return;
        RefreshChoices(masters.People(ledgerId), masters.Payments(ledgerId), masters.MyPersonId(ledgerId));
        Receipts.RefreshNames();
        Transfers.RefreshNames();
    }

    private bool disposed;
    private IReadOnlyList<RecordFilterChoice> owners = [], payments = [];
    public IReadOnlyList<RecordFilterChoice> Owners => owners;
    public IReadOnlyList<RecordFilterChoice> Payments => payments;
    public RecordFilterChoice? Owner => owners.FirstOrDefault(p => p.Id == Search.Receipts.OwnerPersonId);
    public RecordFilterChoice? Payment => payments.FirstOrDefault(p => p.Id == Search.Receipts.PaymentMethodId);

    public string MonthText
    {
        get => Search.Receipts.MonthText;
        set
        {
            if (MonthText != value)
            {
                Search.Receipts.MonthText = value;
                OnPropertyChanged();
            }
        }
    }

    public string Keyword
    {
        get => Search.TransferText;
        set
        {
            if (Keyword != value)
            {
                Search.TransferText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AllPeriods => Search.Receipts.AllPeriods;
    public string? MonthError => Search.Receipts.MonthError;
    public string AppliedMonth => "適用中：" + (AllPeriods ? "全期間" : Search.Receipts.AppliedMonth);
    public bool CanMovePrevious => Search.Receipts.CanMove(-1);
    public bool CanMoveNext => Search.Receipts.CanMove(1);

    private RecordOpeningNotice? receiptOpening, transferOpening;
    public RecordOpeningNotice? ReceiptOpening
    {
        get => receiptOpening; set => SetProperty(ref receiptOpening, value);
    }
    public RecordOpeningNotice? TransferOpening
    {
        get => transferOpening; set => SetProperty(ref transferOpening, value);
    }

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
        var first = Receipts.StartAsync(Search.Receipts.Query(), token);
        return Task.WhenAll(first, disposed ? Task.CompletedTask : Transfers.StartAsync(Search.TransferQuery(), token));
    }

    public Task ResumeAsync(CancellationToken token = default) => disposed ? Task.CompletedTask : !Receipts.HasStarted ? StartAsync(token) : Task.WhenAll(Receipts.ResumeAsync(token), Transfers.ResumeAsync(token));
    public void Suspend()
    {
        Receipts.Suspend();
        Transfers.Suspend();
    }

    private void RefreshChoices(PeopleInfo? people, PaymentMethodsInfo? methods, Guid? mine)
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
        }.Concat((methods?.PaymentMethods ?? []).OrderBy(p => order.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder).Select(p => new RecordFilterChoice(p.PaymentMethodId, Receipts.PaymentDisplayName(p.PaymentMethodId)))).ToArray();
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
        var valid = Search.Receipts.ApplyMonthText();
        RefreshSearch();
        return valid ? StartAsync() : Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ApplyKeywordAsync()
    {
        var valid = Search.ApplySharedText(Keyword);
        RefreshSearch();
        return valid ? StartAsync() : Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MoveMonthAsync(int delta)
    {
        if (!Search.Receipts.MoveMonth(delta))
            return Task.CompletedTask;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetPeriodAsync(bool all)
    {
        Search.Receipts.AllPeriods = all;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetOwnerAsync(RecordFilterChoice choice)
    {
        Search.Receipts.OwnerPersonId = choice.Id;
        return ReloadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SetPaymentAsync(RecordFilterChoice choice)
    {
        Search.Receipts.PaymentMethodId = choice.Id;
        return ReloadAsync();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        masterLifetime?.Cancel();
        masterLifetime?.Dispose();
        masterLifetime = null;
        Receipts.Dispose();
        Transfers.Dispose();
        RetryReceiptRequested = null;
        RetryTransferRequested = null;
    }
}
