using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monetail.Windows.Presentation.Lists;

/// <summary>行と選択、カーソル取得の寿命を所有する。検索条件と通信契約は派生VMに残す。</summary>
public abstract partial class CursorListViewModel<TItem> : ObservableObject, IRecordListState, IDisposable where TItem : class
{
    public bool HasStarted => HasQuery;
    public ListViewportPosition ViewPosition { get; set; }
    private readonly ObservableCollection<TItem> rows = [];
    private ReadOnlyObservableCollection<TItem>? readOnlyRows;
    public ReadOnlyObservableCollection<TItem> Rows => readOnlyRows ??= new(rows);
    private TItem? selectedItem;
    public TItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
                SelectionChanged();
        }
    }
    protected virtual void SelectionChanged()
    {
    }
    protected abstract bool HasQuery
    {
        get;
    }
    protected abstract bool HasFocus
    {
        get;
    }
    protected abstract TItem? FindSavedItem(Guid id);
    protected abstract Task FocusSavedAsync(Guid id, CancellationToken token);
    protected abstract Task RetrySavedAsync(Guid id, CancellationToken token);
    private Guid? savedTarget;
    public bool IsFindingSaved
    {
        get; private set;
    }
    public bool IsFocused => HasFocus && HasLoaded;
    protected abstract Task<CursorPage<TItem>> LoadPageAsync(string? cursor, CancellationToken token);
    protected abstract ReadFailure ClassifyFailure(Exception error, string? cursor);
    protected virtual void ResetReadOrigin()
    {
    }
    public abstract string ErrorText
    {
        get;
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
    public bool CanRetry => Failure is not null && !IsLoading;
    public bool IsEmpty => HasLoaded && Rows.Count == 0 && Failure is null && !IsLoading;
    private CancellationTokenSource? cancellation;
    private long generation;
    private bool recoveredCursor, suspended;
    private sealed record PendingRead(bool Append, bool AutomaticFill);
    private PendingRead? activeRead, interruptedRead;

    protected void ResetPages(bool findingSaved = false)
    {
        Stop(false, false);
        suspended = false;
        ClearRows();
        ViewPosition = default;
        NextCursor = null;
        HasLoaded = false;
        Failure = null;
        AutomaticFillCount = 0;
        ViewportFilled = false;
        recoveredCursor = false;
        IsFindingSaved = findingSaved;
        if (!findingSaved)
            savedTarget = null;
    }
    protected void ClearRows()
    {
        rows.Clear();
        SelectedItem = default;
    }
    public void Suspend() => Stop(true, true);
    private void Stop(bool remember, bool notify)
    {
        IsFindingSaved = false;
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
            OnPropertyChanged(nameof(ViewportFilled));
            OnPropertyChanged(nameof(CanContinue));
            ContinueCommand.NotifyCanExecuteChanged();
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
        : savedTarget is Guid id ? RetrySavedAsync(id, token) : ReadAsync(HasLoaded, token);
    public Task ResumeAsync(CancellationToken token = default)
    {
        if (IsLoading)
            return Task.CompletedTask;
        suspended = false;
        if (Failure is not null || !HasQuery)
            return Task.CompletedTask;
        if (interruptedRead is { } pending)
        {
            interruptedRead = null;
            IsFindingSaved = savedTarget is not null;
            var focused = HasFocus;
            var read = ReadAsync(pending.Append, token, pending.AutomaticFill);
            return savedTarget is Guid id ? FindSavedAsync(read, focused, id, token, generation) : read;
        }
        return !HasLoaded ? ReadAsync(false, token) : Task.CompletedTask;
    }
    protected Task ShowSavedAsync(Guid id, Func<Task> start, bool focused, CancellationToken token)
    {
        savedTarget = id;
        var read = start();
        return FindSavedAsync(read, focused, id, token, generation);
    }
    private async Task FindSavedAsync(Task read, bool focused, Guid id, CancellationToken token, long epoch)
    {
        try
        {
            await read;
            if (!focused)
            {
                while (epoch == generation && !token.IsCancellationRequested && Failure is null && FindSavedItem(id) is null &&
                    NextCursor is not null && AutomaticFillCount < 2)
                    await ReadAsync(true, token, automaticFill: true);
                if (epoch != generation || Failure is not null || token.IsCancellationRequested)
                    return;
                if (FindSavedItem(id) is null)
                {
                    var focus = FocusSavedAsync(id, token);
                    epoch = generation;
                    await focus;
                }
            }
            if (epoch == generation && Failure is null && !token.IsCancellationRequested)
            {
                SelectedItem = FindSavedItem(id);
                savedTarget = null;
            }
        }
        finally
        {
            // 新検索/中断後の旧探索は、新しい一覧状態を変更しない。
            if (epoch == generation)
            {
                IsFindingSaved = false;
                NotifyState();
            }
        }
    }
    protected async Task ReadAsync(bool append, CancellationToken token, bool automaticFill = false)
    {
        if (suspended || IsLoading || !HasQuery || append && NextCursor is null)
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
                var cursor = append ? NextCursor : null;
                try
                {
                    activeRead = new(append, automaticFill && append);
                    var page = await LoadPageAsync(cursor, ct);
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    // 復帰した補充も成功時に一度だけ数え、カーソル復旧の先頭ページは数えない。
                    if (automaticFill && append)
                        AutomaticFillCount++;
                    foreach (var row in page.Rows)
                        rows.Add(row);
                    NextCursor = page.NextCursor;
                    HasLoaded = true;
                    return;
                }
                catch (Exception error) when (ClassifyFailure(error, cursor) != ReadFailure.Unhandled)
                {
                    if (epoch != generation || ct.IsCancellationRequested)
                        return;
                    var failure = ClassifyFailure(error, cursor);
                    if (failure == ReadFailure.Failure)
                    {
                        Failure = error;
                        return;
                    }
                    ResetReadOrigin();
                    if (failure == ReadFailure.InvalidCursor)
                    {
                        ClearRows();
                        HasLoaded = false;
                        NextCursor = null;
                        AutomaticFillCount = 0;
                        if (recoveredCursor || cursor is null)
                        {
                            Failure = error;
                            return;
                        }
                        recoveredCursor = true;
                    }
                    append = false;
                    activeRead = new(false, false);
                    NotifyState();
                }
            }
        }
        catch (OperationCanceledException) when (epoch != generation || ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (epoch == generation)
            {
                IsLoading = false;
                activeRead = null;
                NotifyState();
            }
        }
    }
    protected virtual void NotifyState()
    {
        foreach (var name in new[] { nameof(IsLoading), nameof(HasLoaded), nameof(IsInitialLoading), nameof(IsContinuationLoading),
            nameof(Failure), nameof(IsInitialFailure), nameof(IsContinuationFailure), nameof(NextCursor), nameof(AutomaticFillCount),
            nameof(ViewportFilled), nameof(CanContinue), nameof(CanRetry), nameof(IsEmpty), nameof(ErrorText), nameof(IsFindingSaved), nameof(IsFocused) })
            OnPropertyChanged(name);
        RetryCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
    }
    public void Dispose()
    {
        Stop(false, false);
        // 退避はSuspend、破棄はDispose。nullのバインド更新だけでは古い表示行が残り得る。
        ClearRows();
    }
}

/// <summary>通信DTOではなく、表示行と不透明な次カーソルだけを受け取る。</summary>
public sealed record CursorPage<TItem>(IEnumerable<TItem> Rows, string? NextCursor);

public enum ReadFailure
{
    Unhandled, Failure, InvalidCursor, Restart
}
