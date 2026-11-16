using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Transfers;

public enum TransferSaveKind { Register, Update, ReuseRegistration }
public enum TransferReadOnlyReason { None, Conflict, Forbidden, NotFound }
public sealed record TransferSavePreparation(TransferSaveKind Kind, Guid? TransferId, Guid? Revision, TransferSnapshot Snapshot);
public sealed record TransferDeletePreparation(Guid TransferId, Guid Revision);

/// <summary>一回の補完に対応する条件。呼出側がHTTPへ渡し、同じ文脈の応答だけを適用する。</summary>
public sealed class TransferAutofillContext
{
    public string Name { get; }
    public DateOnly TransactionDate { get; }
    public Guid? FromPaymentMethodId { get; }
    internal TransferAutofillContext(string name, DateOnly date, Guid? from) { Name = name; TransactionDate = date; FromPaymentMethodId = from; }
}

/// <summary>HTTP・時計・requestId発行に依存しない振替編集状態。表示だけでは変更としない。</summary>
public sealed class TransferEditorState
{
    public Guid? TransferId { get; private set; }
    public Guid? Revision { get; private set; }
    public bool IsExisting => TransferId is not null;
    public TransferDraft Draft { get; }
    public TransferReadOnlyReason ReadOnlyReason { get; private set; }
    public bool IsReadOnly => ReadOnlyReason != TransferReadOnlyReason.None;
    public TransferInfo? Latest { get; private set; }
    private TransferAutofillContext? autofill;
    private sealed record Baseline(DateTime At, string Name, Guid? From, Guid? To, int? Amount, string? Memo);
    private Baseline baseline;
    public Guid? SavedFromPaymentMethodId => IsExisting ? baseline.From : null;
    public Guid? SavedToPaymentMethodId => IsExisting ? baseline.To : null;
    public bool HasUnsavedChanges => Draft.HasUnresolvedInput || Draft.TransactionAt != baseline.At || Draft.Name != baseline.Name ||
        Draft.From.PaymentMethodId != baseline.From || Draft.To.PaymentMethodId != baseline.To || Draft.Amount != baseline.Amount || NormalizeMemo(Draft.Memo) != NormalizeMemo(baseline.Memo);
    public bool NeedsLeaveConfirmation => HasUnsavedChanges;
    public bool? IsMidnight => Draft.TransactionAt is DateTime at ? at.TimeOfDay == TimeSpan.Zero : null;
    public bool? HasSavedDateChanged => !IsExisting ? false : Draft.UnresolvedDateText is null && Draft.TransactionDate is DateOnly date ? date != DateOnly.FromDateTime(baseline.At) : null;

    private TransferEditorState(DateTime at, TransferInfo? saved)
    {
        if (saved is not null) ValidateSaved(saved);
        Draft = new(EnsureEditable, () => autofill = null);
        Draft.Load(at, saved?.Name ?? "", saved?.FromPaymentMethodId, saved?.ToPaymentMethodId, saved?.Amount, saved?.Memo, TransferSelectionSource.Saved);
        TransferId = saved?.TransferId; Revision = saved?.Revision; baseline = CaptureBaseline();
    }
    public static TransferEditorState Create(DateOnly initialDate) => new(initialDate.ToDateTime(TimeOnly.MinValue), null);
    public static TransferEditorState Edit(TransferInfo saved) => new(saved.TransactionAt, saved);
    private Baseline CaptureBaseline() => new(Draft.TransactionAt!.Value, Draft.Name, Draft.From.PaymentMethodId, Draft.To.PaymentMethodId, Draft.Amount, Draft.Memo);
    private static string? NormalizeMemo(string? value) => value is "" ? null : value;
    private void EnsureEditable() { if (IsReadOnly) throw new InvalidOperationException("読み取り専用の振替は変更・保存できません。"); }
    public void SelectFrom(Guid? id)
    {
        EnsureEditable(); if (id is Guid value) TransferQueries.Id(value, "fromPaymentMethodId");
        var next = new TransferPaymentSelection(id, id is null ? TransferSelectionSource.Unselected : TransferSelectionSource.Manual);
        if (Draft.From != next) autofill = null; Draft.From = next;
    }
    public void SelectTo(Guid? id)
    {
        EnsureEditable(); if (id is Guid value) TransferQueries.Id(value, "toPaymentMethodId");
        Draft.To = new(id, id is null ? TransferSelectionSource.Unselected : TransferSelectionSource.Manual);
    }
    public TransferAutofillContext? BeginAutofill()
    {
        autofill = null;
        if (IsExisting || IsReadOnly || string.IsNullOrWhiteSpace(Draft.Name) || Draft.Name.Length > 100 || Draft.UnresolvedDateText is not null || Draft.TransactionDate is not DateOnly date) return null;
        return autofill = new(Draft.Name, date, Draft.From.Source == TransferSelectionSource.Manual ? Draft.From.PaymentMethodId : null);
    }
    public bool TryApplyAutofill(TransferAutofillContext context, TransferAutofillInfo result)
    {
        if (!ReferenceEquals(autofill, context) || context is null || IsReadOnly || IsExisting) return false;
        if ((result.FromPaymentMethodId is null) != (result.ToPaymentMethodId is null) ||
            result.FromPaymentMethodId is Guid from && (from == Guid.Empty || result.ToPaymentMethodId == Guid.Empty || from == result.ToPaymentMethodId || context.FromPaymentMethodId is Guid specified && from != specified))
            throw new TransferInputException("autofill", "補完結果を確認してください。");
        static TransferPaymentSelection Apply(TransferPaymentSelection current, Guid? id) => current.Source is TransferSelectionSource.Unselected or TransferSelectionSource.Automatic
            ? new(id, id is null ? TransferSelectionSource.Unselected : TransferSelectionSource.Automatic) : current;
        Draft.From = Apply(Draft.From, result.FromPaymentMethodId); Draft.To = Apply(Draft.To, result.ToPaymentMethodId); autofill = null; return true;
    }
    public TransferSavePreparation PrepareSave(TransferSaveKind kind)
    {
        EnsureEditable();
        if (!Enum.IsDefined(kind) || (kind == TransferSaveKind.Register) == IsExisting) throw new TransferInputException("saveKind", "編集状態に合う保存方式を選択してください。");
        if (Draft.TransactionAt is not DateTime at) throw new TransferInputException("transactionAt", "日時を確定してください。");
        if (Draft.UnresolvedAmountText is not null || Draft.Amount is null) throw new TransferInputException("amount", "金額を確定してください。");
        Guid Required(Guid? id, string field) => id ?? throw new TransferInputException(field, "支払方法を選択してください。");
        var snapshot = new TransferSnapshot(at, Draft.Name, Required(Draft.From.PaymentMethodId, "fromPaymentMethodId"), Required(Draft.To.PaymentMethodId, "toPaymentMethodId"), Draft.Amount.Value, NormalizeMemo(Draft.Memo));
        TransferWriteRequest.ValidateSnapshot(snapshot);
        return new(kind, kind == TransferSaveKind.Update ? TransferId : null, kind == TransferSaveKind.Update ? Revision : null, snapshot);
    }
    public TransferDeletePreparation PrepareDelete()
    {
        EnsureEditable(); if (TransferId is not Guid id || Revision is not Guid version) throw new TransferInputException("transferId", "保存済みの振替を指定してください。");
        return new(id, version);
    }
    public void RegisterConflict(TransferInfo latest)
    {
        if (ReadOnlyReason is TransferReadOnlyReason.Forbidden or TransferReadOnlyReason.NotFound) throw new InvalidOperationException("アクセスできない振替の競合情報は採用できません。");
        if (latest.TransferId != TransferId) throw new TransferInputException("transferId", "同じ振替の最新情報を指定してください。");
        ValidateSaved(latest); Latest = latest with { }; EnterReadOnly(TransferReadOnlyReason.Conflict);
    }
    public bool AdoptLatest(bool confirmed)
    {
        if (!confirmed) return false;
        if (ReadOnlyReason != TransferReadOnlyReason.Conflict || Latest is not TransferInfo latest) throw new InvalidOperationException("採用できる競合情報がありません。");
        ValidateSaved(latest);
        Draft.Load(latest.TransactionAt, latest.Name, latest.FromPaymentMethodId, latest.ToPaymentMethodId, latest.Amount, latest.Memo, TransferSelectionSource.Saved);
        TransferId = latest.TransferId; Revision = latest.Revision; baseline = CaptureBaseline(); Latest = null; autofill = null; ReadOnlyReason = TransferReadOnlyReason.None; return true;
    }
    public void EnterReadOnly(TransferReadOnlyReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == TransferReadOnlyReason.None) throw new ArgumentOutOfRangeException(nameof(reason));
        if (ReadOnlyReason is TransferReadOnlyReason.Forbidden or TransferReadOnlyReason.NotFound && reason == TransferReadOnlyReason.Conflict)
            throw new InvalidOperationException("アクセスできない振替の競合情報は採用できません。");
        ReadOnlyReason = reason; autofill = null;
    }
    private static void ValidateSaved(TransferInfo saved)
    {
        TransferQueries.Id(saved.TransferId, "transferId"); TransferWriteRequest.ValidateRevision(saved.Revision);
        TransferWriteRequest.ValidateSnapshot(new(saved.TransactionAt, saved.Name, saved.FromPaymentMethodId, saved.ToPaymentMethodId, saved.Amount, saved.Memo));
    }
}
