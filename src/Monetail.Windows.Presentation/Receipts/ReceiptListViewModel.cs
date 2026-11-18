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
    private ReceiptListQuery? conditions;
    protected override bool HasQuery => conditions is not null;
    public Task StartAsync(ReceiptListQuery query, CancellationToken token = default)
    {
        ResetPages();
        conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q);
        return ReadAsync(false, token);
    }
    public override void Dispose()
    {
        base.Dispose();
        // 退避はSuspend、破棄はDispose。nullのバインド更新だけでは古い表示行が残り得る。
        ClearRows();
    }
    protected override async Task<CursorPage<ReceiptListItem>> LoadPageAsync(string? cursor, CancellationToken token)
    {
        var query = conditions!;
        var page = await load(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, cursor), token);
        return new(page.Receipts.Select(row => new ReceiptListItem(row, paymentName)), page.NextCursor);
    }
    protected override ReadFailure ClassifyFailure(Exception error, string? cursor) => error switch
    {
        ReceiptRequestException { StatusCode: HttpStatusCode.BadRequest, Code: "invalid_cursor" } => ReadFailure.InvalidCursor,
        ApiConnectionException or ReceiptRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException => ReadFailure.Failure,
        _ => ReadFailure.Unhandled
    };
}
