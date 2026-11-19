using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Receipts;

public enum ReceiptEditorConfirmation
{
    UnappliedDetail, RemoveCategory
}

public sealed partial class ReceiptEditorViewModel
{
    public event Func<ReceiptEditorConfirmation, Task<ReceiptDraftChoice>>? ConfirmationRequested;
    public ReceiptDetailInput? DetailInput
    {
        get; private set;
    }
    public TaxRatesInfo? TaxHistory
    {
        get; set;
    }
    public ReceiptCategoryRow[] Groups { get; private set; } = [];
    public ReceiptDetailRow[] Details { get; private set; } = [];
    private bool validationRequested;
    private (PeopleInfo? People, PaymentMethodsInfo? Payments, CategoriesInfo? Categories, Guid? Mine) Masters => masters?.Invoke() ?? default;
    public bool IsDialogOpen => availability().DialogOpen;
    public bool IsTransitioning => TransitionCommand.IsRunning;
    public bool HasUnappliedInput => DetailInput?.HasChanges == true || Editor.HasUnappliedChanges;
    public ReceiptCategoryRow? SelectedGroup => Groups.FirstOrDefault(g => g.Key == Editor.SelectedGroupKey);
    public ReceiptDetailRow? SelectedDetail => Details.FirstOrDefault(d => d.Key == Editor.SelectedDetailKey);
    public bool CanApply => !IsReadOnly && Editor.SelectedGroup is not null;
    public bool CanChangeDetail => !IsReadOnly && Editor.SelectedDetailKey is not null;
    public bool CanMoveUp => CanChangeDetail && Editor.SelectedGroup?.Details.FirstOrDefault()?.Key != Editor.SelectedDetailKey;
    public bool CanMoveDown => CanChangeDetail && Editor.SelectedGroup?.Details.LastOrDefault()?.Key != Editor.SelectedDetailKey;
    public bool CanRemoveCategory => !IsReadOnly && Editor.SelectedGroup is not null;
    public string ApplyText => Editor.SelectedDetailKey is null ? "追加" : "更新";
    public bool IsIncome => Masters.Categories?.Categories.FirstOrDefault(c => c.CategoryId == Editor.SelectedGroup?.CategoryId)?.IncomeExpense == "Income";
    public ReceiptPaymentChoice[] PaymentChoices()
    {
        var m = Masters;
        return PaymentChoices(m.People, m.Payments, m.Mine, id => paymentName?.Invoke(id) ?? "支払方法名未取得");
    }
    public ReceiptPaymentChoice[] CategoryChoices(ReceiptPaymentChoice? previous, bool preserve)
    {
        var date = Editor.Header.TransactionAt is DateTime at ? DateOnly.FromDateTime(at) : (DateOnly?)null;
        var choices = (Masters.Categories?.Categories ?? []).Where(c => date is DateOnly day &&
            (c.ValidFrom is null || c.ValidFrom <= day) && (c.ValidTo is null || c.ValidTo >= day))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryId).Select(c => new ReceiptPaymentChoice(c.CategoryId, c.Name)).ToList();
        if (preserve && previous is { Id: Guid id } && choices.All(c => c.Id != id))
            choices.Add(previous);
        return choices.ToArray();
    }
    public bool CanAddCategory(Guid? id) => !IsReadOnly && id is Guid key && Editor.Groups.All(g => g.CategoryId != key) &&
        Masters.Categories?.Categories.Any(c => c.CategoryId == key) == true;
    public void RebuildGroups()
    {
        var names = (Masters.Categories?.Categories ?? []).ToDictionary(c => c.CategoryId);
        Groups = Editor.Groups.OrderBy(g => names.GetValueOrDefault(g.CategoryId)?.DisplayOrder ?? int.MaxValue).ThenBy(g => g.CategoryId)
            .Select(g => new ReceiptCategoryRow(g.Key, g.CategoryId, names.GetValueOrDefault(g.CategoryId)?.Name ?? "科目名未取得")).ToArray();
    }
    public void RefreshNames()
    {
        foreach (var row in Groups)
            row.SetName(Masters.Categories?.Categories.FirstOrDefault(c => c.CategoryId == row.CategoryId)?.Name ?? "科目名未取得");
    }
    public void RebuildDetails() => Details = Editor.SelectedGroup?.Details.Select(d => new ReceiptDetailRow(d)).ToArray() ?? [];
    public void Discard()
    {
        DetailInput = null;
        TaxHistory = null;
        Groups = [];
        Details = [];
    }
    public void LoadDetailInput()
    {
        validationRequested = false;
        DetailInput = Editor.Draft is { } draft ? new(draft) : null;
        Refresh();
    }
    public void SynchronizeDetail()
    {
        if (IsReadOnly || DetailInput is null || Editor.Draft is not { } draft)
            return;
        try
        {
            DetailInput.ApplyTo(draft);
        }
        catch (ReceiptInputException) { }
    }
    public bool ApplyDetail()
    {
        if (IsReadOnly || IsDialogOpen || IsTransitioning)
            return false;
        return ApplyDetailCore();
    }
    private bool ApplyDetailCore()
    {
        if (DetailInput is null || Editor.Draft is not { } draft)
            return false;
        try
        {
            DetailInput.ApplyTo(draft);
            Editor.ApplyDraft();
            LoadDetailInput();
            RebuildDetails();
            return true;
        }
        catch (ReceiptInputException)
        {
            validationRequested = true;
            return false;
        }
    }
    public void ReuseDetail()
    {
        if (IsReadOnly || IsDialogOpen || IsTransitioning || Editor.SelectedDetailKey is null)
            return;
        Editor.ReuseSelectedDetail();
        Refresh();
    }
    public bool DeleteDetail()
    {
        if (IsReadOnly || IsDialogOpen || IsTransitioning || Editor.SelectedDetailKey is null)
            return false;
        if (Editor.Draft is { } draft && DetailInput is { } input)
        {
            draft.PersonIds.Clear();
            draft.PersonIds.AddRange(input.Values.PersonIds);
        }
        Editor.DeleteSelectedDetail();
        LoadDetailInput();
        RebuildDetails();
        return true;
    }
    public void MoveDetail(int direction)
    {
        if (IsReadOnly || IsDialogOpen || IsTransitioning)
            return;
        Editor.MoveSelectedDetail(direction);
        RebuildDetails();
        Refresh();
    }
    [RelayCommand]
    private async Task TransitionAsync(ReceiptEditTransition target)
    {
        if (IsReadOnly || IsDialogOpen || target.Kind == ReceiptTransitionKind.AddGroup && !CanAddCategory(target.CategoryId))
            return;
        if (target.Kind == ReceiptTransitionKind.SelectGroup && target.GroupKey == Editor.SelectedGroupKey ||
            target.Kind == ReceiptTransitionKind.SelectDetail && target.DetailKey == Editor.SelectedDetailKey)
            return;
        var input = DetailInput;
        try
        {
            if (HasUnappliedInput)
            {
                var choice = ConfirmationRequested is { } confirm ? await confirm(ReceiptEditorConfirmation.UnappliedDetail) : ReceiptDraftChoice.Stay;
                if (IsReadOnly || choice == ReceiptDraftChoice.Stay)
                    return;
                if (choice == ReceiptDraftChoice.ApplyAndMove)
                {
                    if (!ApplyDetailCore())
                        return;
                }
                else
                {
                    Editor.DiscardDraft();
                    LoadDetailInput();
                }
            }
            if (target.Kind == ReceiptTransitionKind.RemoveGroup)
            {
                var choice = ConfirmationRequested is { } confirm ? await confirm(ReceiptEditorConfirmation.RemoveCategory) : ReceiptDraftChoice.Stay;
                if (IsReadOnly || choice != ReceiptDraftChoice.ApplyAndMove)
                    return;
            }
            try
            {
                switch (target.Kind)
                {
                    case ReceiptTransitionKind.AddGroup:
                        Editor.AddCategory(target.CategoryId!.Value);
                        RebuildGroups();
                        break;
                    case ReceiptTransitionKind.SelectGroup:
                        Editor.SelectGroup(target.GroupKey!.Value);
                        break;
                    case ReceiptTransitionKind.SelectDetail:
                        Editor.SelectDetail(target.DetailKey!.Value);
                        break;
                    case ReceiptTransitionKind.RemoveGroup:
                        Editor.RemoveSelectedGroup(true);
                        RebuildGroups();
                        break;
                }
                LoadDetailInput();
                if (target.Kind != ReceiptTransitionKind.SelectDetail)
                    RebuildDetails();
            }
            catch (ReceiptInputException) when (target.Kind == ReceiptTransitionKind.SelectGroup)
            {
                if (ActionRequested is { } notify)
                    await notify(ReceiptEditorAction.CategoryUnavailable);
            }
        }
        finally
        {
            // 選択確定とフォーム反映の間に非同期の隙間を作らない。
            if (!ReferenceEquals(input, DetailInput))
                OnPropertyChanged(nameof(DetailInput));
        }
    }

    public void SetGroupMemo(string? memo)
    {
        if (!IsReadOnly && Editor.SelectedGroup is { } group && group.Memo != memo)
            Editor.SetGroupMemo(memo);
    }
    public IEnumerable<(Guid Id, string Name)> PeopleChoices()
    {
        var m = Masters;
        var choices = (m.People?.People ?? []).OrderByDescending(p => p.PersonId == m.Mine).ThenBy(p => p.DisplayOrder).ThenBy(p => p.PersonId)
            .Select(p => (Id: p.PersonId, p.Name)).ToList();
        foreach (var id in DetailInput?.Values.PersonIds ?? [])
            if (choices.All(p => p.Id != id))
                choices.Add((id, "人物名未取得"));
        return choices;
    }
    public IEnumerable<(ReceiptTaxSelection Tax, string Label)> TaxChoices(DateOnly? day)
    {
        var selected = DetailInput?.Values.Tax ?? ReceiptTaxSelection.Unselected;
        if (TaxHistory is not { } history)
            return [];
        var choices = ReceiptTaxRules.Candidates(history, day).ToList();
        if (selected.IsSelected && !choices.Contains(selected))
            choices.Add(selected);
        return choices.Select(choice => (choice, ReceiptDetailRow.TaxLabel(choice) + (ReceiptTaxRules.Check(TaxHistory, day, choice) switch
        {
            ReceiptTaxValidity.NeedsReselection => "（要再選択）",
            ReceiptTaxValidity.Unknown => "（日付未確定）",
            _ => ""
        })));
    }
    public (string TaxWarning, string InputError) Calculate(DateOnly? day, bool confirmed)
    {
        var result = ReceiptCalculationDisplay.Evaluate(Editor, TaxHistory, day, confirmed);
        foreach (var row in Groups)
            row.SetAmount(result.Groups.TryGetValue(row.Key, out var amount) ? amount : null);
        foreach (var row in Details)
            row.SetCalculation(result.Details.TryGetValue(row.Key, out var amount) ? amount : null,
            result.Taxes.GetValueOrDefault(row.Key, ReceiptTaxValidity.Unknown));
        ShowCalculation(result);
        var tax = ReceiptTaxRules.Check(TaxHistory, day, DetailInput?.Values.Tax ?? ReceiptTaxSelection.Unselected);
        var warning = DetailInput is null ? "" : tax switch
        {
            ReceiptTaxValidity.Unselected => "税率を選択してください。",
            ReceiptTaxValidity.NeedsReselection => "この税率は取引日に適用できません。値を保持しています。税率を再選択してください。",
            ReceiptTaxValidity.Unknown => "取引日が未確定のため、税率を判定できません。",
            _ => ""
        };
        var errorText = "";
        if (DetailInput is { } input)
            try
            {
                _ = input.Read();
                if (validationRequested)
                    Editor.ValidateDraft();
            }
            catch (ReceiptInputException error)
            {
                if (error.Field != "taxRate" && !error.Field.EndsWith(".taxRate", StringComparison.Ordinal))
                    errorText = error.Message;
            }
        return (warning, errorText);
    }
}
