using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Transfers;

/// <summary>ページングと取得世代だけを管理する。表示領域/利用者スクロールの観測はUIから渡す。</summary>
public sealed class TransferListState(Func<TransferListQuery, CancellationToken, Task<TransferListInfo>> load) : IDisposable
{
    public ObservableCollection<TransferSummaryInfo> Rows { get; } = [];
    public event Action? Changed;
    public Guid? SelectedTransferId { get; set; }
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
    public Guid? FocusTransferId => conditions?.FocusTransferId;
    private TransferListQuery? conditions;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool recoveredCursor, suspended;
    private sealed record PendingRead(bool Append, bool AutomaticFill);
    private PendingRead? activeRead, interruptedRead;
    public Task StartAsync(TransferListQuery query, CancellationToken token = default)
    {
        Stop(false, false); suspended = false; conditions = new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusTransferId: query.FocusTransferId);
        Rows.Clear(); SelectedTransferId = null; NextCursor = null; HasLoaded = false; Failure = null;
        AutomaticFillCount = 0; ViewportFilled = false; recoveredCursor = false;
        return ReadAsync(false, token);
    }
    public async Task ShowSavedAsync(TransferListQuery query, Guid transferId, CancellationToken token = default)
    {
        var first = StartAsync(query, token); var epoch = generation; await first;
        for (var page = 0; page < 2 && epoch == generation && Failure is null && !Rows.Any(r => r.TransferId == transferId) && NextCursor is not null; page++)
            await ReadAsync(true, token, automaticFill: true);
        if (epoch != generation || Failure is not null || token.IsCancellationRequested) return;
        if (Rows.Any(r => r.TransferId == transferId)) { SelectedTransferId = transferId; Changed?.Invoke(); return; }
        var focused = StartAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, focusTransferId: transferId), token);
        epoch = generation; await focused;
        if (epoch == generation && Failure is null && Rows.Any(r => r.TransferId == transferId)) { SelectedTransferId = transferId; Changed?.Invoke(); }
    }
    public Task ReturnToStartAsync(CancellationToken token = default) => conditions is { } query
        ? StartAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q), token) : Task.CompletedTask;
    public void Suspend() => Stop(true, true);
    private void Stop(bool remember, bool notify)
    {
        var wasLoading = IsLoading;
        if (!remember) interruptedRead = null;
        else if (wasLoading) interruptedRead = activeRead;
        generation++; suspended = true;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        activeRead = null; IsLoading = false;
        if (notify && wasLoading) Changed?.Invoke();
    }
    public async Task ObserveViewportAsync(int lastVisible, bool filled, bool userScrolled, CancellationToken token = default)
    {
        if (ViewportFilled != filled) { ViewportFilled = filled; Changed?.Invoke(); }
        if (suspended || !HasLoaded || IsLoading || Failure is not null || NextCursor is null) return;
        if (userScrolled)
        {
            if (lastVisible >= 0 && Rows.Count - 1 - lastVisible <= 20) await ReadAsync(true, token);
        }
        else if (!filled && AutomaticFillCount < 2 && Rows.Count < 300)
            await ReadAsync(true, token, automaticFill: true);
    }
    public Task ContinueAsync(CancellationToken token = default) => CanContinue ? ReadAsync(true, token) : Task.CompletedTask;
    public Task RetryAsync(CancellationToken token = default) => Failure is not null ? ReadAsync(HasLoaded, token) : Task.CompletedTask;
    public Task ResumeAsync(CancellationToken token = default)
    {
        if (IsLoading) return Task.CompletedTask;
        suspended = false;
        if (Failure is not null || conditions is null) return Task.CompletedTask;
        if (interruptedRead is { } pending)
        {
            interruptedRead = null;
            return ReadAsync(pending.Append, token, pending.AutomaticFill);
        }
        return !HasLoaded ? ReadAsync(false, token) : Task.CompletedTask;
    }
    private async Task ReadAsync(bool append, CancellationToken token, bool automaticFill = false)
    {
        if (suspended || IsLoading || conditions is null || append && NextCursor is null) return;
        var epoch = generation;
        cancellation?.Dispose(); cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cancellation.Token; activeRead = new(append, automaticFill); IsLoading = true; Failure = null; Changed?.Invoke();
        try
        {
            while (true)
            {
                var query = new TransferListQuery(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q, append ? NextCursor : null, append ? null : conditions.FocusTransferId);
                try
                {
                    activeRead = new(append, automaticFill && append);
                    var page = await load(query, ct);
                    if (epoch != generation || ct.IsCancellationRequested) return;
                    // 復帰した補充も成功時に一度だけ数え、カーソル復旧の先頭ページは数えない。
                    if (automaticFill && append) AutomaticFillCount++;
                    foreach (var row in page.Transfers) Rows.Add(row);
                    NextCursor = page.NextCursor; HasLoaded = true; return;
                }
                catch (TransferRequestException error) when (error.StatusCode == HttpStatusCode.NotFound && query.FocusTransferId is not null)
                {
                    if (epoch != generation || ct.IsCancellationRequested) return;
                    conditions = new(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q);
                    append = false; activeRead = new(false, false); Changed?.Invoke();
                }
                catch (TransferRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest && error.Code == "invalid_cursor")
                {
                    if (epoch != generation || ct.IsCancellationRequested) return;
                    conditions = new(conditions.Month, conditions.OwnerPersonId, conditions.PaymentMethodId, conditions.Q);
                    Rows.Clear(); SelectedTransferId = null; HasLoaded = false; NextCursor = null; AutomaticFillCount = 0;
                    if (recoveredCursor || query.Cursor is null) { Failure = error; return; }
                    recoveredCursor = true; append = false; activeRead = new(false, false); Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (epoch != generation || ct.IsCancellationRequested) { }
        catch (Exception error) when (error is ApiConnectionException or TransferRequestException or AuthenticationRejectedException or AuthenticationContextChangedException
            or PeopleRequestException or PaymentMethodRequestException or CategoryRequestException or IOException or HttpRequestException
            or OperationCanceledException or UnauthorizedAccessException or CryptographicException or SocketException)
        { if (epoch == generation && !ct.IsCancellationRequested) Failure = error; }
        finally { if (epoch == generation) { IsLoading = false; activeRead = null; Changed?.Invoke(); } }
    }
    public void Dispose() => Stop(false, false);
}
