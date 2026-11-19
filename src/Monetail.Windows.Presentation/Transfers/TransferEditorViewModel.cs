using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
namespace Monetail.Windows.Presentation.Transfers;

public enum TransferEditorAction
{
    Back, Save, Reuse, Delete, RetryWrite, RetryMasters, LoadLatest
}
public enum TransferNoticeLevel
{
    Information, Warning, Error
}
// Windowが所有する通信・認証・ダイアログ状態の読み取り窓口。入力や業務状態を複製しない。
public readonly record struct TransferEditorAvailability(bool Frozen, bool Stopped, bool Sending, bool Unknown,
    bool RetryWrite, bool MastersFailed, bool MastersLoading, bool AdoptLatest, bool DialogOpen);
public sealed record TransferPaymentChoice(Guid Id, string Name);
/// <summary>入力の正本はEditor/Draft。表示・検証とWindowへの操作要求だけを担当する。</summary>
public sealed partial class TransferEditorViewModel(
    TransferEditorState editor,
    Func<TransferEditorAvailability> availability,
    Func<Guid, string> paymentName,
    Func<TransferSuggestionQuery, CancellationToken, Task<TransferSuggestionsInfo>> suggest,
    Func<TransferAutofillQuery, CancellationToken, Task<TransferAutofillInfo>> autofill) : ObservableObject
{
    public TransferEditorState Editor
    {
        get;
    } = editor;
    private PeopleInfo? people;
    private PaymentMethodsInfo? methods;
    public event Func<TransferEditorAction, Task>? ActionRequested;
    public event Action? AuthenticationRequired;
    public string Name => Editor.Draft.Name;
    public string Title => Editor.IsExisting ? "振替の編集" : "振替の登録";
    public string SaveText => Editor.IsExisting ? "更新" : "登録";
    public bool IsFrozen => Editor.IsReadOnly || availability().Frozen;
    public bool IsSending => availability().Sending;
    public bool ShowSave => !(Editor.IsReadOnly || availability().Stopped) || availability().Unknown;
    public bool ShowExistingActions => Editor.IsExisting && !Editor.IsReadOnly && !availability().Stopped;
    public bool ShowRetryWrite => availability().RetryWrite;
    public bool ShowRetryMasters => availability().MastersFailed;
    public bool ShowLoadLatest => availability().AdoptLatest;
    public bool CanDelete => Editor.IsExisting && !IsFrozen;
    public bool CanRetryWrite => ShowRetryWrite && !IsSending && !availability().DialogOpen;
    public bool CanRetryMasters => ShowRetryMasters && !availability().MastersLoading && !availability().DialogOpen;
    public bool CanLoadLatest => ShowLoadLatest && !IsSending && !availability().DialogOpen;
    public bool CanSave
    {
        get
        {
            if (IsFrozen)
                return false;
            try
            {
                PrepareSave(false);
                return true;
            }
            catch (TransferInputException)
            {
                return false;
            }
        }
    }
    public bool CanReuse => Editor.IsExisting && CanSave;
    [ObservableProperty] private string fromName = "";
    [ObservableProperty] private string toName = "";
    [ObservableProperty] private string suggestionError = "";
    [ObservableProperty] private string noticeMessage = "";
    [ObservableProperty] private TransferNoticeLevel noticeLevel;
    [ObservableProperty] private bool noticeOpen;
    [ObservableProperty] private string validationMessage = "";
    public string ToError => !Editor.IsReadOnly && Editor.Draft.From.PaymentMethodId is Guid from && Editor.Draft.To.PaymentMethodId == from
        ? "振替元と振替先は異なる支払方法を選択してください。" : "";
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task BackAsync() => RequestAsync(TransferEditorAction.Back);
    [RelayCommand(CanExecute = nameof(CanSave), AllowConcurrentExecutions = true)]
    private Task SaveAsync() => RequestAsync(TransferEditorAction.Save);
    [RelayCommand(CanExecute = nameof(CanReuse), AllowConcurrentExecutions = true)]
    private Task ReuseAsync() => RequestAsync(TransferEditorAction.Reuse);
    [RelayCommand(CanExecute = nameof(CanDelete), AllowConcurrentExecutions = true)]
    private Task DeleteAsync() => RequestAsync(TransferEditorAction.Delete);
    [RelayCommand(CanExecute = nameof(CanRetryWrite), AllowConcurrentExecutions = true)]
    private Task RetryWriteAsync() => RequestAsync(TransferEditorAction.RetryWrite);
    [RelayCommand(CanExecute = nameof(CanRetryMasters), AllowConcurrentExecutions = true)]
    private Task RetryMastersAsync() => RequestAsync(TransferEditorAction.RetryMasters);
    [RelayCommand(CanExecute = nameof(CanLoadLatest), AllowConcurrentExecutions = true)]
    private Task LoadLatestAsync() => RequestAsync(TransferEditorAction.LoadLatest);
    private Task RequestAsync(TransferEditorAction action) => ActionRequested?.Invoke(action) ?? Task.CompletedTask;
    public Task<TransferSuggestionsInfo> SuggestAsync(TransferSuggestionQuery query, CancellationToken token) => suggest(query, token);
    public Task<TransferAutofillInfo> AutofillAsync(TransferAutofillQuery query, CancellationToken token) => autofill(query, token);
    public void RequireAuthentication() => AuthenticationRequired?.Invoke();
    public void UpdateInput(string dateText, string timeText, string amountText, string memoText)
    {
        if (IsFrozen)
            return;
        var date = DateTime.TryParseExact(dateText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? DateOnly.FromDateTime(day) : (DateOnly?)null;
        var time = TimeOnly.TryParseExact(timeText, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)
            ? clock : (TimeOnly?)null;
        Editor.Draft.SetDate(date, date is null ? dateText : null);
        Editor.Draft.SetTime(time, time is null ? timeText : null);
        Editor.Draft.SetAmount(int.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0 ? amount : null,
            amountText.Length == 0 || amount > 0 ? null : amountText);
        Editor.Draft.Memo = memoText is "" ? null : memoText;
        Refresh();
    }
    public void UpdateMasters(PeopleInfo? currentPeople, PaymentMethodsInfo? currentMethods)
    {
        people = currentPeople;
        methods = currentMethods;
        Refresh();
    }
    public (TransferPaymentChoice[] From, TransferPaymentChoice[] To)? PaymentChoices()
    {
        if (people is null || methods is null)
            return null;
        var day = Editor.Draft.UnresolvedDateText is null ? Editor.Draft.TransactionDate : null;
        var order = people.People.OrderByDescending(p => p.PersonId == methods.MyPersonId)
            .ThenBy(p => p.DisplayOrder).Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        var choices = methods.PaymentMethods.Where(p => TransferEditorRules.CanUse(methods, p.PaymentMethodId) &&
            day is DateOnly date && TransferEditorRules.IsActive(date, p.ValidFrom, p.ValidTo) &&
            people.People.Any(person => person.PersonId == p.OwnerPersonId && TransferEditorRules.IsActive(date, person.ValidFrom, person.ValidTo)))
            .OrderBy(p => order.GetValueOrDefault(p.OwnerPersonId, int.MaxValue)).ThenBy(p => p.DisplayOrder)
            .Select(p => new TransferPaymentChoice(p.PaymentMethodId, paymentName(p.PaymentMethodId))).ToArray();
        TransferPaymentChoice[] IncludeSelection(Guid? selected) => selected is Guid id && choices.All(c => c.Id != id)
            ? [.. choices, new(id, paymentName(id))] : choices;
        return (IncludeSelection(Editor.Draft.From.PaymentMethodId), IncludeSelection(Editor.Draft.To.PaymentMethodId));
    }
    public TransferSavePreparation PrepareSave(bool reuse)
    {
        if (people is null || methods is null)
            throw new TransferInputException("master", "マスタ情報を取得してください。");
        var prepared = Editor.PrepareSave(reuse ? TransferSaveKind.ReuseRegistration : Editor.IsExisting ? TransferSaveKind.Update : TransferSaveKind.Register);
        TransferEditorRules.Validate(prepared.Snapshot, people, methods);
        return prepared;
    }
    public void ShowNotice(string message, TransferNoticeLevel level = TransferNoticeLevel.Error)
    {
        NoticeMessage = message;
        NoticeLevel = level;
        NoticeOpen = true;
    }
    public void Refresh()
    {
        if (people is not null && methods is not null)
        {
            FromName = Editor.Draft.From.PaymentMethodId is Guid from ? paymentName(from) : "";
            ToName = Editor.Draft.To.PaymentMethodId is Guid to ? paymentName(to) : "";
        }
        ValidationMessage = ValidateDisplay();
        if (Editor.IsReadOnly && !NoticeOpen)
            ShowNotice("この振替は読み取り専用です。", TransferNoticeLevel.Information);
        foreach (var name in new[] { nameof(Name), nameof(Title), nameof(SaveText), nameof(IsFrozen), nameof(IsSending), nameof(ShowSave),
            nameof(ShowExistingActions), nameof(ShowRetryWrite), nameof(ShowRetryMasters), nameof(ShowLoadLatest), nameof(ToError) })
            OnPropertyChanged(name);
        SaveCommand.NotifyCanExecuteChanged();
        ReuseCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RetryWriteCommand.NotifyCanExecuteChanged();
        RetryMastersCommand.NotifyCanExecuteChanged();
        LoadLatestCommand.NotifyCanExecuteChanged();
    }
    private string ValidateDisplay()
    {
        var draft = Editor.Draft;
        var errorText = "";
        if (!Editor.IsReadOnly && draft.TransactionAt is DateTime at && draft.From.PaymentMethodId is Guid from &&
            draft.To.PaymentMethodId is Guid to && people is not null && methods is not null)
            try
            {
                TransferEditorRules.Validate(new(at, draft.Name, from, to, draft.Amount ?? 1, draft.Memo), people, methods);
            }
            catch (TransferInputException error)
            {
                errorText = error.Message;
            }
        if (!IsFrozen && errorText.Length == 0)
        {
            if (draft.Name.Length > 100 || draft.Name.Length > 0 && string.IsNullOrWhiteSpace(draft.Name))
                return "振替名は空白のみを避け、100文字以内で入力してください。";
            if (draft.UnresolvedAmountText is not null)
                return "金額は1〜2,147,483,647の整数で入力してください。";
            if (draft.Memo is { } memo && (memo.Length > 100 || memo.IndexOfAny(['\r', '\n', '\v', '\f', '\u0085', '\u2028', '\u2029']) >= 0))
                return "メモは改行なし・100文字以内で入力してください。";
        }
        return errorText;
    }
}
