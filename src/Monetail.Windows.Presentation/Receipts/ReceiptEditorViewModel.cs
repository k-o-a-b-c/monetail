using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Receipts;

public enum ReceiptEditorAction
{
    Back, RetryMasters, CategoryUnavailable
}
public readonly record struct ReceiptEditorAvailability(bool ReadOnly, string Message, bool RetryMasters, bool DialogOpen = false);
public sealed record ReceiptPaymentChoice(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>入力と選択の正本はEditor/既存Input。画面投影と画面内操作だけを担当する。</summary>
public sealed partial class ReceiptEditorViewModel : ObservableObject
{
    public ReceiptEditorState Editor
    {
        get;
    }
    public ReceiptParentInputState ParentInput
    {
        get;
    }
    private readonly Func<ReceiptEditorAvailability> availability;
    private readonly Func<(PeopleInfo? People, PaymentMethodsInfo? Payments, CategoriesInfo? Categories, Guid? Mine)>? masters;
    private readonly Func<Guid, string>? paymentName;
    private readonly string? initialLocation;
    public ReceiptEditorViewModel(ReceiptEditorState editor, Func<ReceiptEditorAvailability> availability,
        Func<(PeopleInfo? People, PaymentMethodsInfo? Payments, CategoriesInfo? Categories, Guid? Mine)>? masters = null,
        Func<Guid, string>? paymentName = null)
    {
        Editor = editor;
        ConfirmedDate = editor.Header.TransactionAt is DateTime initialDate ? DateOnly.FromDateTime(initialDate) : null;
        ParentInput = new(editor.Header, editor.ReceiptId is not null);
        initialLocation = editor.Header.ShopLocation;
        memoInvalid = editor.Header.Memo is { Length: > 100 };
        this.availability = availability;
        this.masters = masters;
        this.paymentName = paymentName;
        TransitionCommand.PropertyChanged += (_, _) => Refresh();
    }
    public event Func<ReceiptEditorAction, Task>? ActionRequested;
    public string Title => Editor.ReceiptId is null ? "レシートの登録" : "レシートの編集";
    public bool IsNew => Editor.ReceiptId is null;
    public bool IsExisting => !IsNew;
    public bool IsReadOnly => recovery != RecoveryPhase.None || availability().ReadOnly;
    public string RecoveryMessage => recovery == RecoveryPhase.None ? availability().Message : recovery switch
    {
        RecoveryPhase.Login => "入力を保持しています。再ログインしてください。",
        RecoveryPhase.Loading => "マスタ情報を読み込み中…",
        RecoveryPhase.Failed => "マスタ情報を取得できませんでした",
        RecoveryPhase.Forbidden => "編集権限がなくなったため、保存できません",
        _ => "レシートが削除されたか、アクセスできなくなりました。"
    };
    public bool ShowRetryMasters => recovery == RecoveryPhase.Failed || availability().RetryMasters;
    [ObservableProperty] private bool memoInvalid;
    [ObservableProperty] private bool intervalInvalid;
    [ObservableProperty] private bool paymentInvalid;
    [ObservableProperty] private string accountingTotal = "会計合計（反映済み明細）：未計算";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalculationNotice))]
    private string? calculationNotice;
    public bool HasCalculationNotice => CalculationNotice is not null;
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task BackAsync() => ActionRequested?.Invoke(ReceiptEditorAction.Back) ?? Task.CompletedTask;
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetryMastersAsync() => ActionRequested?.Invoke(ReceiptEditorAction.RetryMasters) ?? Task.CompletedTask;

    public bool UpdateHeader(string dateText, string timeText, string shopName, string location, string memo,
        Guid? paymentId, int? inclusion, string? rounding, string intervalText, string? unit)
    {
        if (IsReadOnly)
            return false;
        var header = Editor.Header;
        var previousDate = header.TransactionAt?.Date;
        var previousPayment = header.PaymentMethodId;
        ConfirmedDate = DateOnly.TryParseExact(dateText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var confirmed) ? confirmed : null;
        if (DateOnly.TryParseExact(dateText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            TimeOnly.TryParseExact(timeText, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            header.TransactionAt = date.ToDateTime(time);
        header.ShopName = shopName;
        header.ShopLocation = location == (initialLocation ?? "") ? initialLocation : location;
        header.Memo = ReceiptMemoValue.Normalize(memo);
        MemoInvalid = memo.Length > 100;
        // 未選択への暗黙変更は行わない。
        if (paymentId is not null)
            header.PaymentMethodId = paymentId;
        header.TaxInclusionMode = inclusion;
        header.TaxRoundingMode = rounding;
        var recurrence = ReceiptRecurrenceInput.Parse(intervalText, unit);
        if (recurrence.IsValid)
        {
            header.RecurrenceInterval = recurrence.Interval;
            header.RecurrenceUnit = recurrence.Unit;
        }
        IntervalInvalid = !recurrence.IsValid;
        return previousDate != header.TransactionAt?.Date || previousPayment != header.PaymentMethodId;
    }
    public bool HasUnresolvedInput(string date, string time, string interval, string? unit) =>
        date != DateTimeInput.FormatDate(Editor.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : null) ||
        time != DateTimeInput.FormatTime(Editor.Header.TransactionAt is DateTime clock ? TimeOnly.FromDateTime(clock) : null) ||
        !ReceiptRecurrenceInput.Parse(interval, unit).IsValid;

    public ReceiptPaymentChoice[] PaymentChoices(PeopleInfo? people, PaymentMethodsInfo? payments, Guid? mine, Func<Guid, string> name)
    {
        var owners = (people?.People ?? []).OrderByDescending(p => p.PersonId == mine).ThenBy(p => p.DisplayOrder)
            .Select((p, i) => (p.PersonId, i)).ToDictionary(p => p.PersonId, p => p.i);
        var date = Editor.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : (DateOnly?)null;
        bool Valid(DateOnly? from, DateOnly? to) => date is DateOnly day && (from is null || from <= day) && (to is null || to >= day);
        var methods = (payments?.PaymentMethods ?? []).OrderBy(p => owners.GetValueOrDefault(p.OwnerPersonId, int.MaxValue))
            .ThenBy(p => p.DisplayOrder).ThenBy(p => p.PaymentMethodId).ToArray();
        var selected = Editor.Header.PaymentMethodId;
        var choices = methods.Where(p => p.PaymentMethodId == selected || Valid(p.ValidFrom, p.ValidTo))
            .Select(p => new ReceiptPaymentChoice(p.PaymentMethodId, name(p.PaymentMethodId))).ToList();
        if (selected is Guid id && choices.All(c => c.Id != id))
            choices.Add(new(id, "支払方法名未取得"));
        var method = methods.FirstOrDefault(p => p.PaymentMethodId == selected);
        PaymentInvalid = selected is not null && (method is null || !Valid(method.ValidFrom, method.ValidTo));
        return choices.ToArray();
    }
    public void ShowCalculation(ReceiptCalculationDisplay result)
    {
        AccountingTotal = "会計合計（反映済み明細）：" + (result.Amounts?.TotalAmount.ToString("N0", CultureInfo.CurrentCulture) ?? "未計算");
        CalculationNotice = result.Message;
    }
    public void Refresh()
    {
        foreach (var name in new[] { nameof(Title), nameof(IsNew), nameof(IsExisting), nameof(IsReadOnly), nameof(RecoveryMessage), nameof(ShowRetryMasters), nameof(IsTransitioning), nameof(CanApply), nameof(CanChangeDetail),
            nameof(CanMoveUp), nameof(CanMoveDown), nameof(CanRemoveCategory), nameof(ApplyText) })
            OnPropertyChanged(name);
        BackCommand.NotifyCanExecuteChanged();
        RetryMastersCommand.NotifyCanExecuteChanged();
    }
}
