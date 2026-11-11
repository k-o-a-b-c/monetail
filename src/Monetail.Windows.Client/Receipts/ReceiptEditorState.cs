using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Receipts;

/// <summary>編集モデルと未反映フォームを分離する。HTTP・時計・マスタ取得・税計算は持たない。</summary>
public sealed class ReceiptEditorState
{
    public Guid? ReceiptId { get; }
    public Guid? Revision { get; }
    public ReceiptHeaderDraft Header { get; }
    private readonly List<ReceiptEditGroup> groups = [];
    public IReadOnlyList<ReceiptEditGroup> Groups { get; }
    public Guid? SelectedGroupKey { get; private set; }
    public Guid? SelectedDetailKey { get; private set; }
    public ReceiptEditGroup? SelectedGroup => groups.FirstOrDefault(g => g.Key == SelectedGroupKey);
    public ReceiptDetailDraft? Draft { get; private set; }
    public ReceiptEditTransition? PendingTransition { get; private set; }
    private ReceiptDetailDraft? draftBaseline;
    private Guid[] rememberedPeople = [];
    private readonly Func<Guid, DateTime?, ReceiptCategoryDefaults> defaults;
    private readonly Action<ReceiptDetailSnapshot>? validateDetail;
    private readonly Action<ReceiptSnapshot>? validateSnapshot;
    private readonly ReceiptHeaderDraft baselineHeader;
    private readonly Dictionary<Guid, GroupBaseline> baselineGroups;
    private sealed record DetailBaseline(Guid? Id, int Order, ReceiptDetailDraft Values);
    private sealed record GroupBaseline(Guid? Id, Guid Category, string? Memo, DetailBaseline[] Details);
    public bool HasUnappliedChanges => Draft is not null && draftBaseline is not null && !Draft.Same(draftBaseline);
    public bool HasUnsavedChanges => !Header.Same(baselineHeader) || !SameGroups();
    public bool NeedsLeaveConfirmation => HasUnappliedChanges || HasUnsavedChanges;

    private ReceiptEditorState(ReceiptHeaderDraft header, ReceiptInfo? receipt, Func<Guid, DateTime?, ReceiptCategoryDefaults> defaults,
        Action<ReceiptDetailSnapshot>? validateDetail, Action<ReceiptSnapshot>? validateSnapshot)
    {
        this.defaults = defaults ?? throw new ArgumentNullException(nameof(defaults)); this.validateDetail = validateDetail; this.validateSnapshot = validateSnapshot;
        Header = header.Copy(); ReceiptId = receipt?.ReceiptId; Revision = receipt?.Revision;
        if (receipt is not null)
        {
            ReceiptQueries.Id(receipt.ReceiptId, "receiptId"); ReceiptQueries.Id(receipt.Revision, "revision");
            foreach (var g in receipt.Groups) groups.Add(new(g.GroupId, g.CategoryId, g.Memo, g.Details.Select(d => new ReceiptEditDetail(d.DetailId, d.DisplayOrder, ReceiptDetailDraft.From(d)))));
        }
        Groups = groups.AsReadOnly(); baselineHeader = Header.Copy();
        baselineGroups = groups.Select(g => new GroupBaseline(g.GroupId, g.CategoryId, g.Memo, g.Rows.Select(d => new DetailBaseline(d.DetailId, d.OriginalOrder, d.Stored.Copy())).ToArray())).ToDictionary(g => g.Category);
    }
    public static ReceiptEditorState Create(ReceiptHeaderDraft initial, Func<Guid, DateTime?, ReceiptCategoryDefaults> defaults,
        Action<ReceiptDetailSnapshot>? validateDetail = null, Action<ReceiptSnapshot>? validateSnapshot = null) => new(initial, null, defaults, validateDetail, validateSnapshot);
    public static ReceiptEditorState Edit(ReceiptInfo receipt, Func<Guid, DateTime?, ReceiptCategoryDefaults> defaults,
        Action<ReceiptDetailSnapshot>? validateDetail = null, Action<ReceiptSnapshot>? validateSnapshot = null) => new(new ReceiptHeaderDraft {
            TransactionAt = receipt.TransactionAt, ShopName = receipt.ShopName, ShopLocation = receipt.ShopLocation, PaymentMethodId = receipt.PaymentMethodId,
            TaxInclusionMode = receipt.TaxInclusionMode, TaxRoundingMode = receipt.TaxRoundingMode, RecurrenceInterval = receipt.RecurrenceInterval, RecurrenceUnit = receipt.RecurrenceUnit, Memo = receipt.Memo
        }, receipt, defaults, validateDetail, validateSnapshot);

    public bool AddCategory(Guid categoryId) { Ready(); ReceiptQueries.Id(categoryId, "categoryId"); return Request(new(ReceiptTransitionKind.AddGroup, CategoryId: categoryId)); }
    public bool SelectGroup(Guid key) { Ready(); if (key == SelectedGroupKey) return true; return Request(new(ReceiptTransitionKind.SelectGroup, GroupKey: key)); }
    public bool SelectDetail(Guid key) { Ready(); if (key == SelectedDetailKey) return true; return Request(new(ReceiptTransitionKind.SelectDetail, SelectedGroupKey, key)); }
    public bool RemoveSelectedGroup(bool confirmed)
    {
        if (!confirmed || SelectedGroup is null) return false;
        Ready(); return Request(new(ReceiptTransitionKind.RemoveGroup, SelectedGroupKey));
    }
    private bool Request(ReceiptEditTransition transition)
    {
        CheckTarget(transition);
        if (HasUnappliedChanges) { PendingTransition = transition; return false; }
        Execute(transition, false); return true;
    }
    public void ResolveTransition(ReceiptDraftChoice choice)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        var target = PendingTransition ?? throw new InvalidOperationException("保留中の切替がありません。");
        if (choice == ReceiptDraftChoice.Stay) { PendingTransition = null; return; }
        try { Execute(target, choice == ReceiptDraftChoice.ApplyAndMove); }
        finally { PendingTransition = null; }
    }
    private void CheckTarget(ReceiptEditTransition target)
    {
        if (target.Kind == ReceiptTransitionKind.AddGroup)
        {
            if (groups.Any(g => g.CategoryId == target.CategoryId)) throw new ReceiptInputException("categoryId", "追加済みの科目です。");
        }
        else
        {
            var group = groups.SingleOrDefault(g => g.Key == target.GroupKey) ?? throw new ReceiptInputException("categoryId", "科目を選択してください。");
            if (target.Kind == ReceiptTransitionKind.SelectDetail && (group.Key != SelectedGroupKey || group.Rows.All(d => d.Key != target.DetailKey)))
                throw new ReceiptInputException("detailId", "選択中の科目の明細を指定してください。");
        }
    }
    private void Execute(ReceiptEditTransition target, bool apply)
    {
        CheckTarget(target);
        var prepared = apply ? PrepareDetail() : null;
        var people = prepared?.PersonIds.ToArray() ?? Draft?.PersonIds.ToArray() ?? rememberedPeople;
        ReceiptEditGroup? group = target.Kind switch {
            ReceiptTransitionKind.AddGroup => new(baselineGroups.GetValueOrDefault(target.CategoryId!.Value)?.Id, target.CategoryId!.Value, null, []),
            ReceiptTransitionKind.RemoveGroup => null,
            _ => groups.Single(g => g.Key == target.GroupKey)
        };
        var row = target.Kind == ReceiptTransitionKind.SelectDetail ? group!.Rows.Single(d => d.Key == target.DetailKey) : null;
        // 次の初期値取得も先に済ませ、失敗時に元明細を更新しない。
        var next = row is not null ? row.Stored.Copy() : group is not null ? NewDraft(group.CategoryId, people) : null;
        if (prepared is not null) CommitDetail(prepared);
        if (target.Kind == ReceiptTransitionKind.AddGroup) groups.Add(group!);
        if (target.Kind == ReceiptTransitionKind.RemoveGroup) groups.RemoveAll(g => g.Key == target.GroupKey);
        rememberedPeople = people.ToArray(); SelectedGroupKey = group?.Key; SelectedDetailKey = row?.Key; SetDraft(next);
    }
    private ReceiptDetailDraft NewDraft(Guid category, IEnumerable<Guid> people)
    {
        var settings = defaults(category, Header.TransactionAt);
        var expense = settings.IncomeExpense switch { "Income" => "Excluded", "Expense" => "Variable", _ => throw new ReceiptInputException("categoryId", "科目の収支区分を確認してください。") };
        var draft = new ReceiptDetailDraft { ExpenseType = expense, Tax = settings.Tax }; draft.PersonIds.AddRange(people); return draft;
    }
    private void SetDraft(ReceiptDetailDraft? value) { Draft = value; draftBaseline = value?.Copy(); }
    public void SetGroupMemo(string? memo) { Ready(); var group = SelectedGroup ?? throw new ReceiptInputException("categoryId", "科目を選択してください。"); group.Memo = memo; }

    private ReceiptDetailDraft PrepareDetail()
    {
        var group = SelectedGroup ?? throw new ReceiptInputException("categoryId", "科目を選択してください。");
        var draft = (Draft ?? throw new ReceiptInputException("detailDraft", "明細を入力してください。")).Copy();
        var id = group.Rows.FirstOrDefault(d => d.Key == SelectedDetailKey)?.DetailId;
        var row = draft.Snapshot(id, 0, "detailDraft"); ReceiptWriteRequest.ValidateDetail(row, "detailDraft");
        var income = defaults(group.CategoryId, Header.TransactionAt).IncomeExpense;
        if (income == "Income" && row.ExpenseType != "Excluded")
            throw new ReceiptInputException("detailDraft.expenseType", "科目の収支区分に合う費用区分を選択してください。");
        validateDetail?.Invoke(row); return draft;
    }
    private void CommitDetail(ReceiptDetailDraft value)
    {
        var group = SelectedGroup!;
        if (SelectedDetailKey is Guid key) group.Rows.Single(d => d.Key == key).Stored = value.Copy();
        else group.Rows.Add(new(null, group.Rows.Count, value));
    }
    public void ApplyDraft()
    {
        Ready(); var prepared = PrepareDetail(); var next = NewDraft(SelectedGroup!.CategoryId, prepared.PersonIds);
        CommitDetail(prepared); SelectedDetailKey = null; rememberedPeople = prepared.PersonIds.ToArray(); SetDraft(next);
    }
    public void ReuseSelectedDetail()
    {
        Ready(); if (SelectedDetailKey is null || Draft is null) return;
        var baseline = NewDraft(SelectedGroup!.CategoryId, Draft.PersonIds);
        SelectedDetailKey = null; draftBaseline = baseline;
    }
    public bool DeleteSelectedDetail()
    {
        Ready(); if (SelectedGroup is not { } group || SelectedDetailKey is not Guid key) return false;
        var next = NewDraft(group.CategoryId, Draft!.PersonIds);
        group.Rows.RemoveAll(d => d.Key == key); SelectedDetailKey = null; rememberedPeople = Draft.PersonIds.ToArray(); SetDraft(next); return true;
    }
    public bool MoveSelectedDetail(int direction)
    {
        Ready(); if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (SelectedGroup is not { } group || SelectedDetailKey is not Guid key) return false;
        var index = group.Rows.FindIndex(d => d.Key == key); var next = index + direction;
        if (next < 0 || next >= group.Rows.Count) return false;
        (group.Rows[index], group.Rows[next]) = (group.Rows[next], group.Rows[index]); return true;
    }
    public void DiscardDraft()
    {
        Ready(); if (SelectedGroup is { } group) { var next = NewDraft(group.CategoryId, Draft?.PersonIds ?? rememberedPeople.AsEnumerable()); SelectedDetailKey = null; SetDraft(next); }
    }
    public bool RefreshNewDraftDefaults()
    {
        Ready(); if (SelectedGroup is not { } group || SelectedDetailKey is not null || HasUnappliedChanges) return false;
        var next = NewDraft(group.CategoryId, Draft?.PersonIds ?? rememberedPeople.AsEnumerable()); SetDraft(next); return true;
    }
    private void Ready() { if (PendingTransition is not null) throw new InvalidOperationException("保留中の切替を確定または取り消してください。"); }

    public ReceiptSavePreparation PrepareSave(ReceiptSaveKind kind)
    {
        Ready(); if (!Enum.IsDefined(kind) || (kind == ReceiptSaveKind.Register) != (ReceiptId is null)) throw new ReceiptInputException("saveKind", "編集状態に合う保存方式を選択してください。");
        if (HasUnappliedChanges) throw new ReceiptInputException("detailDraft", "明細を追加/更新するか、入力を明示的に破棄してください。");
        T Required<T>(T? value, string field) where T : struct => value ?? throw new ReceiptInputException(field, "入力してください。");
        var keepIds = kind == ReceiptSaveKind.Update;
        var snapshot = new ReceiptSnapshot(Required(Header.TransactionAt, "transactionAt"), Header.ShopName, Header.ShopLocation, Required(Header.PaymentMethodId, "paymentMethodId"),
            Required(Header.TaxInclusionMode, "taxInclusionMode"), Header.TaxRoundingMode!, Header.RecurrenceInterval, Header.RecurrenceUnit, Header.Memo,
            groups.Select((g, i) => CaptureGroup(g, i, keepIds)).ToArray());
        ReceiptWriteRequest.ValidateSnapshot(snapshot); validateSnapshot?.Invoke(snapshot);
        return new(kind, keepIds ? ReceiptId : null, keepIds ? Revision : null, snapshot);
    }
    private static ReceiptGroupSnapshot CaptureGroup(ReceiptEditGroup group, int index, bool keepIds)
    {
        var original = group.IsOriginalOrder;
        return new(keepIds ? group.GroupId : null, group.CategoryId, group.Memo, group.Rows.Select((d, i) =>
            d.Stored.Snapshot(keepIds ? d.DetailId : null, group.Order(i, original), $"groups[{index}].details[{i}]")).ToArray());
    }
    private bool SameGroups()
    {
        if (groups.Count != baselineGroups.Count) return false;
        foreach (var current in groups)
        {
            var original = baselineGroups.GetValueOrDefault(current.CategoryId);
            var orderUnchanged = current.IsOriginalOrder;
            if (original is null || original.Id != current.GroupId || original.Memo != current.Memo || original.Details.Length != current.Rows.Count) return false;
            for (var i = 0; i < current.Rows.Count; i++)
                if (original.Details[i].Id != current.Rows[i].DetailId || original.Details[i].Order != current.Order(i, orderUnchanged) || !original.Details[i].Values.Same(current.Rows[i].Stored)) return false;
        }
        return true;
    }
}
