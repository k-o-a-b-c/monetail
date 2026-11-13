using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptEditorStateTests
{
    private static readonly DateOnly TaxDay = new(2026, 11, 10);
    private static TaxRatesInfo History(params TaxRateInfo[] rows) => new(rows);
    [Fact]
    public void TaxCandidatesAndDefaultsDistinguishEmptyNullZeroAndMissingPeriods()
    {
        var empty = History();
        Assert.Equal(new[] { ReceiptTaxSelection.Exempt }, ReceiptTaxRules.Candidates(empty, TaxDay));
        Assert.Equal(ReceiptTaxSelection.Exempt, ReceiptTaxRules.Initial(empty, "Exempt", TaxDay));
        Assert.False(ReceiptTaxRules.Initial(empty, "Standard", TaxDay).IsSelected);
        var history = History(new TaxRateInfo("Standard", TaxDay, TaxDay, null), new("ReducedFood", TaxDay, null, 0), new("ReducedNewspaper", TaxDay, null, 0));
        Assert.Equal(ReceiptTaxSelection.Exempt, ReceiptTaxRules.Initial(history, "Standard", TaxDay));
        Assert.Equal(ReceiptTaxSelection.Taxed(0), ReceiptTaxRules.Initial(history, "ReducedFood", TaxDay));
        Assert.Equal(new[] { ReceiptTaxSelection.Exempt, ReceiptTaxSelection.Taxed(0) }, ReceiptTaxRules.Candidates(history, TaxDay));
        Assert.False(ReceiptTaxRules.Initial(history, "Standard", TaxDay.AddDays(1)).IsSelected);
        Assert.False(ReceiptTaxRules.Initial(history, "Standard", TaxDay.AddDays(-1)).IsSelected);
    }
    [Fact]
    public void TaxRulesUseInclusiveDatesDistinctNumericOrderAndNeverAnotherDaysRate()
    {
        var history = History(new TaxRateInfo("Standard", TaxDay, TaxDay, 10), new("ReducedFood", TaxDay, null, 8.00m),
            new("ReducedNewspaper", TaxDay, null, 8), new("Standard", TaxDay.AddDays(1), null, 1));
        Assert.Equal(new[] { ReceiptTaxSelection.Exempt, ReceiptTaxSelection.Taxed(8), ReceiptTaxSelection.Taxed(10) }, ReceiptTaxRules.Candidates(history, TaxDay));
        Assert.Equal(ReceiptTaxValidity.NeedsReselection, ReceiptTaxRules.Check(history, TaxDay.AddDays(1), ReceiptTaxSelection.Taxed(10)));
        Assert.Equal(ReceiptTaxValidity.Valid, ReceiptTaxRules.Check(history, TaxDay.AddDays(1), ReceiptTaxSelection.Exempt));
        Assert.Equal(ReceiptTaxValidity.NeedsReselection, ReceiptTaxRules.Check(history, TaxDay, ReceiptTaxSelection.Taxed(0)));
        Assert.Equal(ReceiptTaxValidity.Unknown, ReceiptTaxRules.Check(history, null, ReceiptTaxSelection.Taxed(10)));
    }
    [Fact]
    public void FormalDisplayIgnoresUnappliedDraftButOriginalCalculationEntryStillRejectsIt()
    {
        var editor = New(); editor.AddCategory(Category); Fill(editor); editor.ApplyDraft();
        var history = History(new TaxRateInfo("Standard", DateOnly.MinValue, null, 10));
        var before = ReceiptCalculationDisplay.Evaluate(editor, history, TaxDay, true); Assert.NotNull(before.Amounts);
        editor.Draft!.Name = "未反映"; editor.Draft.UnitPrice = int.MaxValue;
        var snapshot = JsonSerializer.Serialize(editor.Groups); var draft = editor.Draft; var key = editor.SelectedGroupKey;
        Assert.Throws<ReceiptInputException>(() => ReceiptCalculations.FromEditor(editor));
        var after = ReceiptCalculationDisplay.Evaluate(editor, history, TaxDay, true);
        Assert.Equal(before.Amounts!.TotalAmount, after.Amounts!.TotalAmount); Assert.Same(draft, editor.Draft);
        Assert.Equal(snapshot, JsonSerializer.Serialize(editor.Groups)); Assert.Equal(key, editor.SelectedGroupKey); Assert.True(editor.HasUnappliedChanges);
    }
    private static ReceiptEditorState AppliedTaxes(params (int Price, decimal? Tax)[] rows)
    {
        var editor = New(); editor.AddCategory(Category);
        foreach (var (price, tax) in rows)
        {
            Fill(editor); editor.Draft!.UnitPrice = price; editor.Draft.Tax = tax is decimal rate ? ReceiptTaxSelection.Taxed(rate) : ReceiptTaxSelection.Exempt;
            editor.ApplyDraft();
        }
        return editor;
    }
    [Theory]
    [InlineData(0, "floor", 4)] [InlineData(0, "half_up", 4)] [InlineData(1, "floor", 4)] [InlineData(1, "half_up", 4)]
    public void FormalAmountsUseSharedRulesForSignedAmountsAndRounding(int inclusion, string rounding, int total)
    {
        var editor = AppliedTaxes((5, 10), (-1, 10)); editor.Header.TaxInclusionMode = inclusion; editor.Header.TaxRoundingMode = rounding;
        var result = ReceiptCalculationDisplay.Evaluate(editor, History(new TaxRateInfo("Standard", DateOnly.MinValue, null, 10)), TaxDay, true);
        Assert.Equal(total, result.Amounts!.TotalAmount);
        Assert.Equal(JsonSerializer.Serialize(ReceiptCalculations.Calculate(editor)), JsonSerializer.Serialize(result.Amounts));
        Assert.Equal(2, result.Details.Count); Assert.Single(result.Groups);
    }
    [Fact]
    public void FormalAccountingIsNotTheSumOfCategoryReferences()
    {
        var editor = AppliedTaxes((19, 10)); editor.Header.TaxRoundingMode = "floor";
        editor.AddCategory(OtherCategory); Fill(editor); editor.Draft!.Tax = ReceiptTaxSelection.Taxed(10); editor.Draft.ExpenseType = "Excluded"; editor.Draft.UnitPrice = 19; editor.ApplyDraft();
        var result = ReceiptCalculationDisplay.Evaluate(editor, History(new TaxRateInfo("Standard", DateOnly.MinValue, null, 10)), TaxDay, true);
        Assert.Equal(41, result.Amounts!.TotalAmount); Assert.Equal(40, result.Groups.Values.Sum()); Assert.Equal(40, result.Details.Values.Sum());
    }
    [Theory]
    [InlineData("empty")] [InlineData("int-overflow")] [InlineData("decimal-overflow")] [InlineData("header")]
    public void IncompleteOrImpossibleCalculationNeverReturnsZeroOrPreviousAmounts(string kind)
    {
        var rate = kind == "decimal-overflow" ? decimal.MaxValue : 10m;
        var editor = kind == "empty" ? New() : AppliedTaxes((int.MaxValue, rate));
        if (kind == "empty") editor.AddCategory(Category);
        var result = ReceiptCalculationDisplay.Evaluate(editor, History(new TaxRateInfo("Standard", DateOnly.MinValue, null, rate)), TaxDay, kind != "header");
        Assert.Null(result.Amounts); Assert.Empty(result.Groups); Assert.Empty(result.Details); Assert.NotEmpty(result.Message!);
    }
    [Fact]
    public void DateChangesKeepStoredAndReusedRatesAndExposeAllMismatchesWithoutMutation()
    {
        var editor = AppliedTaxes((100, 10), (10, null), (10, 0));
        editor.SelectDetail(editor.Groups[0].Details[0].Key); editor.ReuseSelectedDetail();
        var history = History(new TaxRateInfo("Standard", TaxDay, TaxDay, 10), new("ReducedFood", TaxDay, TaxDay, 0));
        var before = JsonSerializer.Serialize(editor.Groups); var draft = editor.Draft; var draftRate = draft!.Tax; var selected = editor.SelectedGroupKey;
        var mismatch = ReceiptCalculationDisplay.Evaluate(editor, history, TaxDay.AddDays(1), true);
        Assert.Null(mismatch.Amounts); Assert.Equal(2, mismatch.Taxes.Values.Count(v => v == ReceiptTaxValidity.NeedsReselection));
        Assert.Equal(1, mismatch.Taxes.Values.Count(v => v == ReceiptTaxValidity.Valid));
        Assert.Equal(before, JsonSerializer.Serialize(editor.Groups)); Assert.Same(draft, editor.Draft); Assert.Equal(draftRate, editor.Draft!.Tax); Assert.Equal(selected, editor.SelectedGroupKey);
        Assert.Null(ReceiptCalculationDisplay.Evaluate(editor, history, null, false).Amounts);
        Assert.All(ReceiptCalculationDisplay.Evaluate(editor, history, null, false).Taxes.Values.Where(v => v != ReceiptTaxValidity.Valid), v => Assert.Equal(ReceiptTaxValidity.Unknown, v));
    }
    [Fact]
    public void NormalNextDraftUsesCategoryDefaultButReuseKeepsTheActualRate()
    {
        var history = History(new TaxRateInfo("Standard", DateOnly.MinValue, null, 10));
        var editor = ReceiptEditorState.Create(Header(), (_, date) => new("Expense", ReceiptTaxRules.Initial(history, "Standard", DateOnly.FromDateTime(date!.Value))));
        editor.AddCategory(Category); Assert.Equal(ReceiptTaxSelection.Taxed(10), editor.Draft!.Tax);
        Fill(editor); editor.Draft.Tax = ReceiptTaxSelection.Exempt; editor.ApplyDraft();
        Assert.Equal(ReceiptTaxSelection.Taxed(10), editor.Draft!.Tax);
        editor.SelectDetail(editor.Groups[0].Details[0].Key); editor.ReuseSelectedDetail(); Assert.Equal(ReceiptTaxSelection.Exempt, editor.Draft!.Tax);
    }

}
