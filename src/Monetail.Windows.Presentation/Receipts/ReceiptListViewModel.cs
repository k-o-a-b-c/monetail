using Monetail.Windows.Presentation.Lists;
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
    Func<Guid, string> paymentName) : CursorListViewModel<ReceiptListItem>
{
    protected override void SelectionChanged() => OnPropertyChanged(nameof(SelectedReceiptId));
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
    // 取消済みの旧入力GETが未完了でも再度開けるよう、実際の二重起動はWindowの入口ガードで防ぐ。
    [RelayCommand(CanExecute = nameof(CanRegister), AllowConcurrentExecutions = true)]
    private Task RegisterAsync() => OpenRequested?.Invoke(null) ?? Task.CompletedTask;
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task EditAsync(Guid receiptId) => OpenRequested?.Invoke(receiptId) ?? Task.CompletedTask;
    public void RefreshNames()
    {
        foreach (var row in Rows)
            row.RefreshName();
    }
    public override string ErrorText => Failure switch
    {
        ReceiptRequestException { StatusCode: HttpStatusCode.NotFound } => "この家計簿は開けません。参加状態を確認してください。",
        ReceiptRequestException { StatusCode: HttpStatusCode.Forbidden } => "この操作を行う権限がありません。",
        ReceiptRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "操作回数の上限に達しました。しばらく待って再試行してください。",
        PeopleRequestException or PaymentMethodRequestException => "マスタ情報を取得できませんでした。参加状態と権限を確認してください。",
        _ => "レシートを取得できませんでした"
    };
    public Guid? FocusReceiptId => conditions?.FocusReceiptId;
    private ReceiptListQuery? conditions;
    protected override bool HasQuery => conditions is not null;
    public Task StartAsync(ReceiptListQuery query, CancellationToken token = default)
    {
        return StartReadAsync(query, token);
    }
    public override void Dispose()
    {
        base.Dispose();
        // 退避はSuspend、破棄はDispose。nullのバインド更新だけでは古い表示行が残り得る。
        ClearRows();
    }
    private Task StartReadAsync(ReceiptListQuery query, CancellationToken token, bool findingSaved = false)
    {
        ResetPages(findingSaved);
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusReceiptId: query.FocusReceiptId);
        return ReadAsync(false, token);
    }
    public Task ShowSavedAsync(ReceiptListQuery query, Guid receiptId, CancellationToken token = default) =>
        base.ShowSavedAsync(receiptId, () => StartReadAsync(query, token, findingSaved: true), query.FocusReceiptId is not null, token);
    protected override bool HasFocus => conditions?.FocusReceiptId is not null;
    protected override ReceiptListItem? FindSavedItem(Guid id) => Rows.FirstOrDefault(row => row.ReceiptId == id);
    protected override Task FocusSavedAsync(Guid id, CancellationToken token)
    {
        var query = conditions!;
        return StartReadAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusReceiptId: id), token, findingSaved: true);
    }
    protected override Task RetrySavedAsync(Guid id, CancellationToken token) => ShowSavedAsync(search.Receipts.Query(), id, token);
    [RelayCommand]
    public Task ReturnToStartAsync(CancellationToken token = default) => conditions is { } query
        ? StartAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q), token) : Task.CompletedTask;
    protected override void ResetReadOrigin()
    {
        var query = conditions!;
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q);
    }
    protected override void NotifyState()
    {
        base.NotifyState();
        OnPropertyChanged(nameof(FocusReceiptId));
    }
    protected override async Task<CursorPage<ReceiptListItem>> LoadPageAsync(string? cursor, CancellationToken token)
    {
        var query = conditions!;
        var page = await load(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, cursor, cursor is null ? query.FocusReceiptId : null), token);
        return new(page.Receipts.Select(row => new ReceiptListItem(row, paymentName)), page.NextCursor);
    }
    protected override ReadFailure ClassifyFailure(Exception error, string? cursor) => error switch
    {
        ReceiptRequestException { StatusCode: HttpStatusCode.NotFound } when cursor is null && conditions?.FocusReceiptId is not null => ReadFailure.Restart,
        ReceiptRequestException { StatusCode: HttpStatusCode.BadRequest, Code: "invalid_cursor" } => ReadFailure.InvalidCursor,
        ApiConnectionException or ReceiptRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException => ReadFailure.Failure,
        _ => ReadFailure.Unhandled
    };
}
