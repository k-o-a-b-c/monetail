using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Receipts;

/// <summary>一覧の行・選択・取得世代を一元管理し、WinUIの可視範囲だけを受け取る。</summary>
public sealed partial class ReceiptListViewModel(
    Func<ReceiptListQuery, CancellationToken, Task<ReceiptListInfo>> load,
    RecordSearchState search,
    Func<Guid, string> paymentName) : ObservableObject, IDisposable
{
    private readonly ObservableCollection<ReceiptListItem> rows = [];
    private ReadOnlyObservableCollection<ReceiptListItem>? readOnlyRows;
    public ReadOnlyObservableCollection<ReceiptListItem> Rows => readOnlyRows ??= new(rows);
    private ReceiptListItem? selectedItem;
    public ReceiptListItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
                OnPropertyChanged(nameof(SelectedReceiptId));
        }
    }
    public Guid? SelectedReceiptId => SelectedItem?.ReceiptId;
    public string SearchText
    {
        get => search.Receipts.ShopText;
        set
        {
            if (search.Receipts.ShopText == value)
                return;
            search.Receipts.ShopText = value;
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
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() => SearchRequested?.Invoke() ?? Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanRegister), AllowConcurrentExecutions = true)]
    private Task RegisterAsync() => OpenRequested?.Invoke(null) ?? Task.CompletedTask;
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task EditAsync(Guid receiptId) => OpenRequested?.Invoke(receiptId) ?? Task.CompletedTask;
    public void RefreshNames()
    {
        foreach (var row in rows)
            row.RefreshName();
    }
    public bool IsEmpty => HasLoaded && Rows.Count == 0 && Failure is null && !IsLoading;
    public bool CanRetry => Failure is not null && !IsLoading;
    public string ErrorText => Failure switch
    {
        ReceiptRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
        ReceiptRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
        ReceiptRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
        PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
        _ => "レシートを取得できませんでした"
    };
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
    private ReceiptListQuery? conditions;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool recoveredCursor, suspended;
    private sealed record PendingRead(bool Append, bool AutomaticFill);
    private PendingRead? activeRead, interruptedRead;
    public Task StartAsync(ReceiptListQuery query, CancellationToken token = default)
    {
        Stop(false, false);
        suspended = false;
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q);
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
    public void Suspend() => Stop(true, true);
    private void Stop(bool remember, bool notify)
    {
        var wasLoading = IsLoading;
        if (!remember)
            interruptedRead = null;
        else if (wasLoading)
            interruptedRead = activeRead;
        generation++;
        suspended = true;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        activeRead = null;
        IsLoading = false;
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
        if (suspended || !HasLoaded || IsLoading || Failure is not null || NextCursor is null)
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
    public Task RetryAsync(CancellationToken token = default) => Failure is not null ? ReadAsync(HasLoaded, token) : Task.CompletedTask;
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
            return ReadAsync(pending.Append, token, pending.AutomaticFill);
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
                var query = new ReceiptListQuery(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q, append ? NextCursor : null);
                try
                {
                    activeRead = new(append, automaticFill && append);
                    var page = await load(query, ct);
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    // 復帰した補充も成功時に一度だけ数え、カーソル復旧の先頭ページは数えない。
                    if (automaticFill && append)
                        AutomaticFillCount++;
                    foreach (var row in page.Receipts)
                        rows.Add(new(row, paymentName));
                    NextCursor = page.NextCursor;
                    HasLoaded = true;
                    return;
                }
                catch (ReceiptRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest && error.Code == "invalid_cursor")
                {
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
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
        catch (Exception error) when (error is ApiConnectionException or ReceiptRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
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
            nameof(ViewportFilled), nameof(CanContinue), nameof(CanRetry), nameof(IsEmpty), nameof(ErrorText) })
            OnPropertyChanged(name);
        RetryCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() => Stop(false, false);
}
