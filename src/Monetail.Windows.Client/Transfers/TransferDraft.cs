namespace Monetail.Windows.Client.Transfers;

public enum TransferSelectionSource { Unselected, Manual, Automatic, Saved }
public sealed record TransferPaymentSelection
{
    public Guid? PaymentMethodId { get; }
    public TransferSelectionSource Source { get; }
    internal TransferPaymentSelection(Guid? id, TransferSelectionSource source) { PaymentMethodId = id; Source = source; }
}

/// <summary>値と未確定の生文字列を分ける。解析・補完・フォーカス確定は呼出側が行う。</summary>
public sealed class TransferDraft
{
    private readonly Action editable;
    private readonly Action invalidateAutofill;
    private string name = "";
    private string? memo;
    public string Name
    {
        get => name;
        set { editable(); if (name != value) invalidateAutofill(); name = value; }
    }
    public string? Memo { get => memo; set { editable(); memo = value; } }
    public DateOnly? TransactionDate { get; private set; }
    public TimeOnly? TransactionTime { get; private set; }
    public int? Amount { get; private set; }
    public string? UnresolvedDateText { get; private set; }
    public string? UnresolvedTimeText { get; private set; }
    public string? UnresolvedAmountText { get; private set; }
    public TransferPaymentSelection From { get; internal set; } = new(null, TransferSelectionSource.Unselected);
    public TransferPaymentSelection To { get; internal set; } = new(null, TransferSelectionSource.Unselected);
    public bool HasUnresolvedInput => UnresolvedDateText is not null || UnresolvedTimeText is not null || UnresolvedAmountText is not null;
    public DateTime? TransactionAt => UnresolvedDateText is null && UnresolvedTimeText is null && TransactionDate is DateOnly date && TransactionTime is TimeOnly time
        ? date.ToDateTime(time, DateTimeKind.Unspecified) : null;

    internal TransferDraft(Action editable, Action invalidateAutofill) { this.editable = editable; this.invalidateAutofill = invalidateAutofill; }
    public void SetDate(DateOnly? value, string? unresolvedText = null)
    {
        editable(); if (TransactionDate != value || UnresolvedDateText != unresolvedText) invalidateAutofill();
        TransactionDate = value; UnresolvedDateText = unresolvedText;
    }
    public void SetTime(TimeOnly? value, string? unresolvedText = null) { editable(); TransactionTime = value; UnresolvedTimeText = unresolvedText; }
    public void SetAmount(int? value, string? unresolvedText = null) { editable(); Amount = value; UnresolvedAmountText = unresolvedText; }
    internal void Load(DateTime at, string name, Guid? from, Guid? to, int? amount, string? memo, TransferSelectionSource source)
    {
        TransactionDate = DateOnly.FromDateTime(at); TransactionTime = TimeOnly.FromDateTime(at); this.name = name; this.memo = memo; Amount = amount;
        From = new(from, from is null ? TransferSelectionSource.Unselected : source); To = new(to, to is null ? TransferSelectionSource.Unselected : source);
        UnresolvedDateText = null; UnresolvedTimeText = null; UnresolvedAmountText = null;
    }
}
