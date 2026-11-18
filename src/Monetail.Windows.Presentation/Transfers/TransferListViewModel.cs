using Monetail.Windows.Presentation.Lists;
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
    Func<Guid, string> paymentName) : CursorListViewModel<TransferListItem>
{
    protected override void SelectionChanged() => OnPropertyChanged(nameof(SelectedTransferId));
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
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() => SearchRequested?.Invoke() ?? Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanRegister))]
    private Task RegisterAsync() => OpenRequested?.Invoke(null) ?? Task.CompletedTask;
    [RelayCommand]
    private Task EditAsync(Guid transferId) => OpenRequested?.Invoke(transferId) ?? Task.CompletedTask;
    public void RefreshNames()
    {
        foreach (var row in Rows)
            row.RefreshName();
    }
    public Guid? FocusTransferId => conditions?.FocusTransferId;
    public bool IsFocused => FocusTransferId is not null && HasLoaded;
    public bool IsFindingSaved
    {
        get; private set;
    }
    private Guid? savedTarget;
    public override string ErrorText => Failure switch
    {
        TransferRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
        TransferRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
        TransferRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
        PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
        _ => "振替を取得できませんでした"
    };
    private TransferListQuery? conditions;
    protected override bool HasQuery => conditions is not null;
    public Task StartAsync(TransferListQuery query, CancellationToken token = default)
    {
        savedTarget = null;
        IsFindingSaved = false;
        return StartReadAsync(query, token);
    }
    private Task StartReadAsync(TransferListQuery query, CancellationToken token, bool findingSaved = false)
    {
        ResetPages();
        IsFindingSaved = findingSaved;
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusTransferId: query.FocusTransferId);
        return ReadAsync(false, token);
    }
    public Task ShowSavedAsync(TransferListQuery query, Guid transferId, CancellationToken token = default)
    {
        savedTarget = transferId;
        var first = StartReadAsync(query, token, findingSaved: true);
        return FindSavedAsync(first, query, transferId, token, Generation);
    }
    private async Task FindSavedAsync(Task read, TransferListQuery query, Guid transferId, CancellationToken token, long epoch)
    {
        try
        {
            await read;
            if (query.FocusTransferId is null)
            {
                while (epoch == Generation && Failure is null && !Rows.Any(r => r.TransferId == transferId) &&
                    NextCursor is not null && AutomaticFillCount < 2)
                    await ReadAsync(true, token, automaticFill: true);
                if (epoch != Generation || Failure is not null || token.IsCancellationRequested)
                    return;
                if (!Rows.Any(r => r.TransferId == transferId))
                {
                    var focused = StartReadAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q,
                        focusTransferId: transferId), token, findingSaved: true);
                    epoch = Generation;
                    await focused;
                }
            }
            if (epoch == Generation && Failure is null && !token.IsCancellationRequested)
            {
                SelectedItem = Rows.FirstOrDefault(r => r.TransferId == transferId);
                savedTarget = null;
            }
        }
        finally
        {
            // 検索変更後の旧探索は、新しい一覧状態を触らない。
            if (epoch == Generation)
            {
                IsFindingSaved = false;
                NotifyState();
            }
        }
    }
    [RelayCommand]
    public Task ReturnToStartAsync(CancellationToken token = default) => conditions is { } query
        ? StartAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q), token) : Task.CompletedTask;
    protected override bool CanObserveViewport => !IsFindingSaved;
    protected override void Stop(bool remember, bool notify)
    {
        IsFindingSaved = false;
        base.Stop(remember, notify);
    }
    public override Task RetryAsync(CancellationToken token = default) => Failure is null ? Task.CompletedTask
        : savedTarget is Guid id ? ShowSavedAsync(search.TransferQuery(), id, token) : base.RetryAsync(token);
    public override Task ResumeAsync(CancellationToken token = default)
    {
        if (IsLoading)
            return Task.CompletedTask;
        var resumeSaved = Failure is null && conditions is not null && HasInterruptedRead && savedTarget is not null;
        if (resumeSaved)
            IsFindingSaved = true;
        var query = conditions;
        var read = base.ResumeAsync(token);
        return resumeSaved && savedTarget is Guid id
            ? FindSavedAsync(read, query!, id, token, Generation) : read;
    }
    protected override void NotifyState()
    {
        base.NotifyState();
        OnPropertyChanged(nameof(IsFocused));
        OnPropertyChanged(nameof(FocusTransferId));
        OnPropertyChanged(nameof(IsFindingSaved));
    }
    protected override void ResetReadOrigin()
    {
        var query = conditions!;
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q);
    }
    protected override async Task<CursorPage<TransferListItem>> LoadPageAsync(string? cursor, CancellationToken token)
    {
        var query = conditions!;
        var page = await load(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, cursor, cursor is null ? query.FocusTransferId : null), token);
        return new(page.Transfers.Select(row => new TransferListItem(row, paymentName)), page.NextCursor);
    }
    protected override ReadFailure ClassifyFailure(Exception error, string? cursor) => error switch
    {
        TransferRequestException { StatusCode: HttpStatusCode.NotFound } when cursor is null && conditions?.FocusTransferId is not null => ReadFailure.Restart,
        TransferRequestException { StatusCode: HttpStatusCode.BadRequest, Code: "invalid_cursor" } => ReadFailure.InvalidCursor,
        ApiConnectionException or TransferRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException => ReadFailure.Failure,
        _ => ReadFailure.Unhandled
    };
}
