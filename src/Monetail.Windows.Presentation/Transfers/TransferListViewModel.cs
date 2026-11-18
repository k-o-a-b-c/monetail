using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Transfers;

/// <summary>振替一覧の行・選択・取得世代を一元管理する。WinUIの観測値だけを受け取る。</summary>
public sealed partial class TransferListViewModel(
    Func<TransferListQuery, CancellationToken, Task<TransferListInfo>> load,
    RecordSearchState search,
    Func<Guid, string> paymentName) : ObservableObject, IDisposable
{
    private readonly ObservableCollection<TransferListItem> rows = [];
    private ReadOnlyObservableCollection<TransferListItem>? readOnlyRows;
    public ReadOnlyObservableCollection<TransferListItem> Rows => readOnlyRows ??= new(rows);
    private TransferListItem? selectedItem;
    public TransferListItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
                OnPropertyChanged(nameof(SelectedTransferId));
        }
    }
    public Guid? SelectedTransferId => SelectedItem?.TransferId;
    public string SearchText
    {
        get => search.TransferText;
        set
        {
            if (search.TransferText == value)
                return;
            search.TransferText = value;
            OnPropertyChanged();
        }
    }
    public void RefreshSearchText() => OnPropertyChanged(nameof(SearchText));
    private bool canRegister;
    public bool CanRegister
    {
        get => canRegister;
        set
        {
            if (SetProperty(ref canRegister, value))
                RegisterCommand.NotifyCanExecuteChanged();
        }
    }
    public event Func<Task>? SearchRequested;
    public event Func<Guid?, Task>? OpenRequested;
    [RelayCommand]
    private Task SearchAsync() => SearchRequested?.Invoke() ?? Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanRegister))]
    private Task RegisterAsync() => OpenRequested?.Invoke(null) ?? Task.CompletedTask;
    [RelayCommand]
    private Task EditAsync(Guid transferId) => OpenRequested?.Invoke(transferId) ?? Task.CompletedTask;
    public void RefreshNames()
    {
        foreach (var row in rows)
            row.RefreshName();
    }
    public bool IsLoading
    {
        get; private set;
    }
    public bool HasLoaded
    {
        get; private set;
    }
    public bool IsInitialLoading => IsLoading && !HasLoaded;
    public bool IsContinuationLoading => IsLoading && HasLoaded;
    public Exception? Failure
    {
        get; private set;
    }
    public bool IsInitialFailure => Failure is not null && !HasLoaded;
    public bool IsContinuationFailure => Failure is not null && HasLoaded;
    public string? NextCursor
    {
        get; private set;
    }
    public int AutomaticFillCount
    {
        get; private set;
    }
    public bool ViewportFilled
    {
        get; private set;
    }
    public bool CanContinue => HasLoaded && !IsLoading && Failure is null && NextCursor is not null && !ViewportFilled && Rows.Count >= 300;
    public Guid? FocusTransferId => conditions?.FocusTransferId;
    public bool IsFocused => FocusTransferId is not null && HasLoaded;
    public bool IsEmpty => HasLoaded && Rows.Count == 0 && Failure is null && !IsLoading;
    public bool CanRetry => Failure is not null && !IsLoading;
    public bool IsFindingSaved
    {
        get; private set;
    }
    private Guid? savedTarget;
    public string ErrorText => Failure switch
    {
        TransferRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
        TransferRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
        TransferRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
        PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
        _ => "振替を取得できませんでした"
    };
    private TransferListQuery? conditions;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool recoveredCursor, suspended;
    private sealed record PendingRead(bool Append, bool AutomaticFill);
    private PendingRead? activeRead, interruptedRead;
    public Task StartAsync(TransferListQuery query, CancellationToken token = default)
    {
        savedTarget = null;
        IsFindingSaved = false;
        return StartReadAsync(query, token);
    }
    private Task StartReadAsync(TransferListQuery query, CancellationToken token, bool findingSaved = false)
    {
        Stop(false, false);
        IsFindingSaved = findingSaved;
        suspended = false;
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusTransferId: query.FocusTransferId);
        rows.Clear();
        SelectedItem = null;
        NextCursor = null;
        HasLoaded = false;
        Failure = null;
        AutomaticFillCount = 0;
        ViewportFilled = false;
        recoveredCursor = false;
        return ReadAsync(false, token);
    }
    public Task ShowSavedAsync(TransferListQuery query, Guid transferId, CancellationToken token = default)
    {
        savedTarget = transferId;
        var first = StartReadAsync(query, token, findingSaved: true);
        return FindSavedAsync(first, query, transferId, token, generation);
    }
    private async Task FindSavedAsync(Task read, TransferListQuery query, Guid transferId, CancellationToken token, long epoch)
    {
        try
        {
            await read;
            if (query.FocusTransferId is null)
            {
                while (epoch == generation && Failure is null && !Rows.Any(r => r.TransferId == transferId) &&
                    NextCursor is not null && AutomaticFillCount < 2)
                    await ReadAsync(true, token, automaticFill: true);
                if (epoch != generation || Failure is not null || token.IsCancellationRequested)
                    return;
                if (!Rows.Any(r => r.TransferId == transferId))
                {
                    var focused = StartReadAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q,
                        focusTransferId: transferId), token, findingSaved: true);
                    epoch = generation;
                    await focused;
                }
            }
            if (epoch == generation && Failure is null && !token.IsCancellationRequested)
                SelectSaved(transferId);
        }
        finally
        {
            // 検索変更後の旧探索は、新しい一覧状態を触らない。
            if (epoch == generation)
            {
                IsFindingSaved = false;
                NotifyState();
            }
        }
    }
    private void SelectSaved(Guid id)
    {
        if (Rows.FirstOrDefault(r => r.TransferId == id) is not { } row)
            return;
        SelectedItem = row;
        savedTarget = null;
    }
    [RelayCommand]
    public Task ReturnToStartAsync(CancellationToken token = default) => conditions is { } query
        ? StartAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q), token) : Task.CompletedTask;
    public void Suspend() => Stop(true, true);
    private void Stop(bool remember, bool notify)
    {
        var wasLoading = IsLoading;
        if (!remember)
        {
            interruptedRead = null;
            IsFindingSaved = false;
        }
        else if (wasLoading)
            interruptedRead = activeRead;
        generation++;
        suspended = true;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        activeRead = null;
        IsLoading = false;
        IsFindingSaved = false;
        if (notify && wasLoading)
            NotifyState();
    }
    public async Task ObserveViewportAsync(int lastVisible, bool filled, bool userScrolled, CancellationToken token = default)
    {
        if (ViewportFilled != filled)
        {
            ViewportFilled = filled;
            NotifyState();
        }
        if (IsFindingSaved || suspended || !HasLoaded || IsLoading || Failure is not null || NextCursor is null)
            return;
        if (userScrolled)
        {
            if (lastVisible >= 0 && Rows.Count - 1 - lastVisible <= 20)
                await ReadAsync(true, token);
        }
        else if (!filled && AutomaticFillCount < 2 && Rows.Count < 300)
            await ReadAsync(true, token, automaticFill: true);
    }
    [RelayCommand(CanExecute = nameof(CanContinue))]
    public Task ContinueAsync(CancellationToken token = default) => CanContinue ? ReadAsync(true, token) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanRetry))]
    public Task RetryAsync(CancellationToken token = default) => Failure is null ? Task.CompletedTask
        : savedTarget is Guid id ? ShowSavedAsync(search.TransferQuery(), id, token) : ReadAsync(HasLoaded, token);
    public Task ResumeAsync(CancellationToken token = default)
    {
        if (IsLoading)
            return Task.CompletedTask;
        suspended = false;
        if (Failure is not null || conditions is null)
            return Task.CompletedTask;
        if (interruptedRead is { } pending)
        {
            interruptedRead = null;
            IsFindingSaved = savedTarget is not null;
            var read = ReadAsync(pending.Append, token, pending.AutomaticFill);
            return savedTarget is Guid id ? FindSavedAsync(read, conditions, id, token, generation) : read;
        }
        return !HasLoaded ? ReadAsync(false, token) : Task.CompletedTask;
    }
    private async Task ReadAsync(bool append, CancellationToken token, bool automaticFill = false)
    {
        if (suspended || IsLoading || conditions is null || append && NextCursor is null)
            return;
        var epoch = generation;
        cancellation?.Dispose();
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cancellation.Token;
        activeRead = new(append, automaticFill);
        IsLoading = true;
        Failure = null;
        NotifyState();
        try
        {
            while (true)
            {
                var query = new TransferListQuery(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q, append ? NextCursor : null, append ? null : conditions.FocusTransferId);
                try
                {
                    activeRead = new(append, automaticFill && append);
                    var page = await load(query, ct);
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    // 復帰した補充も成功時に一度だけ数え、カーソル復旧の先頭ページは数えない。
                    if (automaticFill && append)
                        AutomaticFillCount++;
                    foreach (var row in page.Transfers)
                        rows.Add(new(row, paymentName));
                    NextCursor = page.NextCursor;
                    HasLoaded = true;
                    return;
                }
                catch (TransferRequestException error) when (error.StatusCode == HttpStatusCode.NotFound && query.FocusTransferId is not null)
                {
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    conditions = new(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q);
                    append = false;
                    activeRead = new(false, false);
                    NotifyState();
                }
                catch (TransferRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest && error.Code == "invalid_cursor")
                {
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    conditions = new(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q);
                    rows.Clear();
                    SelectedItem = null;
                    HasLoaded = false;
                    NextCursor = null;
                    AutomaticFillCount = 0;
                    if (recoveredCursor || query.Cursor is null)
                    {
                        Failure = error;
                        return;
                    }
                    recoveredCursor = true;
                    append = false;
                    activeRead = new(false, false);
                    NotifyState();
                }
            }
        }
        catch (OperationCanceledException) when (epoch != generation || ct.IsCancellationRequested) { }
        catch (Exception error) when (error is ApiConnectionException or TransferRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException)
        {
            if (epoch == generation && !ct.IsCancellationRequested)
                Failure = error;
        }
        finally { if (epoch == generation) { IsLoading = false; activeRead = null; NotifyState(); } }
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(IsLoading), nameof(HasLoaded), nameof(IsInitialLoading), nameof(IsContinuationLoading),
            nameof(Failure), nameof(IsInitialFailure), nameof(IsContinuationFailure), nameof(NextCursor), nameof(AutomaticFillCount),
            nameof(ViewportFilled), nameof(CanContinue), nameof(CanRetry), nameof(IsFocused), nameof(FocusTransferId),
            nameof(IsEmpty), nameof(ErrorText), nameof(IsFindingSaved) })
            OnPropertyChanged(name);
        RetryCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() => Stop(false, false);
}
