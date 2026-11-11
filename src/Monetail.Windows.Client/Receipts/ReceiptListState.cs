using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Receipts;

/// <summary>ページングと取得世代だけを管理する。表示領域/利用者スクロールの観測はUIから渡す。</summary>
public sealed class ReceiptListState(Func<ReceiptListQuery, CancellationToken, Task<ReceiptListInfo>> load) : IDisposable
{
    public ObservableCollection<ReceiptSummaryInfo> Rows { get; } = [];
    public event Action? Changed;
    public Guid? SelectedReceiptId { get; set; }
    public bool IsLoading { get; private set; }
    public bool HasLoaded { get; private set; }
    public bool IsInitialLoading => IsLoading && !HasLoaded;
    public bool IsContinuationLoading => IsLoading && HasLoaded;
    public Exception? Failure { get; private set; }
    public bool IsInitialFailure => Failure is not null && !HasLoaded;
    public bool IsContinuationFailure => Failure is not null && HasLoaded;
    public string? NextCursor { get; private set; }
    public int AutomaticFillCount { get; private set; }
    public bool ViewportFilled { get; private set; }
    public bool CanContinue => HasLoaded && !IsLoading && Failure is null && NextCursor is not null && !ViewportFilled && Rows.Count >= 300;
    private ReceiptListQuery? conditions;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool recoveredCursor;
    public Task StartAsync(ReceiptListQuery query, CancellationToken token = default)
    {
        Suspend(); conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q);
        Rows.Clear(); SelectedReceiptId = null; NextCursor = null; HasLoaded = false; Failure = null;
        AutomaticFillCount = 0; ViewportFilled = false; recoveredCursor = false;
        return ReadAsync(false, token);
    }
    public void Suspend()
    {
        generation++; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; IsLoading = false;
    }
    public async Task ObserveViewportAsync(int lastVisible, bool filled, bool userScrolled, CancellationToken token = default)
    {
        if (ViewportFilled != filled) { ViewportFilled = filled; Changed?.Invoke(); }
        if (!HasLoaded || IsLoading || Failure is not null || NextCursor is null) return;
        if (userScrolled)
        {
            if (lastVisible >= 0 && Rows.Count - 1 - lastVisible <= 20) await ReadAsync(true, token);
        }
        else if (!filled && AutomaticFillCount < 2 && Rows.Count < 300)
        {
            var epoch = generation; var cursorWasRecovered = recoveredCursor;
            await ReadAsync(true, token);
            // 画面移動で中断した要求は補充済みページとして数えない。
            if (epoch == generation && Failure is null && !token.IsCancellationRequested && cursorWasRecovered == recoveredCursor) { AutomaticFillCount++; Changed?.Invoke(); }
        }
    }
    public Task ContinueAsync(CancellationToken token = default) => CanContinue ? ReadAsync(true, token) : Task.CompletedTask;
    public Task RetryAsync(CancellationToken token = default) => Failure is not null ? ReadAsync(HasLoaded, token) : Task.CompletedTask;
    public Task ResumeAsync(CancellationToken token = default) => !HasLoaded && Failure is null && conditions is not null && !IsLoading ? ReadAsync(false, token) : Task.CompletedTask;
    private async Task ReadAsync(bool append, CancellationToken token)
    {
        if (IsLoading || conditions is null || append && NextCursor is null) return;
        var epoch = generation;
        cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cancellation.Token; IsLoading = true; Failure = null; Changed?.Invoke();
        try
        {
            while (true)
            {
                var query = new ReceiptListQuery(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q, append ? NextCursor : null);
                try
                {
                    var page = await load(query, ct);
                    if (epoch != generation || ct.IsCancellationRequested) return;
                    foreach (var row in page.Receipts) Rows.Add(row);
                    NextCursor = page.NextCursor; HasLoaded = true; return;
                }
                catch (ReceiptRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest && error.Code == "invalid_cursor")
                {
                    if (epoch != generation || ct.IsCancellationRequested) return;
                    Rows.Clear(); SelectedReceiptId = null; HasLoaded = false; NextCursor = null; AutomaticFillCount = 0;
                    if (recoveredCursor || query.Cursor is null) { Failure = error; return; }
                    recoveredCursor = true; append = false; Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (epoch != generation || ct.IsCancellationRequested) { }
        catch (Exception error) when (error is ApiConnectionException or ReceiptRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException)
        { if (epoch == generation && !ct.IsCancellationRequested) Failure = error; }
        finally { if (epoch == generation) { IsLoading = false; Changed?.Invoke(); } }
    }
    public void Dispose() => Suspend();
}
