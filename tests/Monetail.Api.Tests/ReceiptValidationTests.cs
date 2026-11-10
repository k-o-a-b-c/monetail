using System.Globalization;
using System.Text.Json;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;
using Xunit;
using static Monetail.Api.Tests.ReceiptCalculationTestData;

namespace Monetail.Api.Tests;

public sealed class ReceiptValidationTests
{
    [Fact]
    public void MissingInputOrMasterSnapshotCannotBypassValidation()
    {
        Assert.Throws<ArgumentNullException>(() => ReceiptCalculator.Calculate(null!));
        Assert.Throws<ArgumentNullException>(() => ReceiptValidation.ValidateForSave(Input(Group(Detail(100))), null!));
    }

    [Theory]
    [InlineData("transactionAt", "2026-02-30T12:00:00")]
    [InlineData("transactionAt", "2026-11-10T12:00")]
    [InlineData("transactionAt", "2026-11-10T12:00:00Z")]
    [InlineData("transactionAt", "2026-11-10T12:00:00+09:00")]
    [InlineData("transactionAt", "2026-11-10T12:00:00.1")]
    [InlineData("transactionAt", "0000-01-01T00:00:00")]
    [InlineData("transactionAt", null)]
    [InlineData("shopName", "")]
    [InlineData("shopName", null)]
    [InlineData("taxInclusionMode", "2")]
    [InlineData("taxInclusionMode", "-1")]
    [InlineData("taxRoundingMode", "FLOOR")]
    [InlineData("taxRoundingMode", null)]
    [InlineData("recurrenceInterval", "0")]
    [InlineData("recurrenceInterval", "-1")]
    [InlineData("recurrenceInterval", "2147483648")]
    [InlineData("recurrenceUnit", "Day")]
    [InlineData("recurrenceUnit", null)]
    public void InvalidHeaderInputsAreRejectedWithoutChanges(string field, string? value)
    {
        var input = Input(Group(Detail(100)));
        input = field switch
        {
            "transactionAt" => input with { TransactionAt = value! }, "shopName" => input with { ShopName = value! },
            "taxInclusionMode" => input with { TaxInclusionMode = int.Parse(value!, CultureInfo.InvariantCulture) },
            "taxRoundingMode" => input with { TaxRoundingMode = value! },
            "recurrenceInterval" => input with { RecurrenceInterval = long.Parse(value!, CultureInfo.InvariantCulture), RecurrenceUnit = "Month" },
            _ => input with { RecurrenceInterval = 1, RecurrenceUnit = value }
        };
        Reject(input, field);
    }

    [Theory]
    [InlineData("unitPrice", "2147483648")]
    [InlineData("unitPrice", "-2147483649")]
    [InlineData("unitPrice", "9223372036854775807")]
    [InlineData("quantity", "0")]
    [InlineData("quantity", "-1")]
    [InlineData("quantity", "2147483648")]
    [InlineData("unitDiscountAmount", "2147483648")]
    [InlineData("unitDiscountAmount", "-2147483649")]
    [InlineData("displayOrder", "-1")]
    [InlineData("displayOrder", "2147483648")]
    [InlineData("discountRate", "-0.1")]
    [InlineData("discountRate", "100.01")]
    [InlineData("discountRate", "NaN")]
    [InlineData("discountRate", "")]
    [InlineData("discountRate", "1,0")]
    [InlineData("discountRate", "1e1")]
    [InlineData("discountRate", "0.00000000000000000000000000001")]
    [InlineData("discountRate", "1.00000000000000000000000000001")]
    [InlineData("discountRate", null)]
    [InlineData("taxRate", "Infinity")]
    [InlineData("taxRate", "NaN")]
    [InlineData("taxRate", "10x")]
    [InlineData("taxRate", "1e1")]
    [InlineData("taxRate", "79228162514264337593543950336")]
    [InlineData("taxRate", "0.00000000000000000000000000001")]
    [InlineData("expenseType", "unknown")]
    [InlineData("occurrenceType", "unknown")]
    [InlineData("name", null)]
    public void InvalidDetailInputsAreRejectedWithoutClampingOrNormalization(string field, string? value)
    {
        var detail = Detail(100);
        detail = field switch
        {
            "unitPrice" => detail with { UnitPrice = long.Parse(value!, CultureInfo.InvariantCulture) },
            "quantity" => detail with { Quantity = long.Parse(value!, CultureInfo.InvariantCulture) },
            "unitDiscountAmount" => detail with { UnitDiscountAmount = long.Parse(value!, CultureInfo.InvariantCulture) },
            "displayOrder" => detail with { DisplayOrder = long.Parse(value!, CultureInfo.InvariantCulture) },
            "discountRate" => detail with { DiscountRate = value! }, "taxRate" => detail with { TaxRate = value },
            "expenseType" => detail with { ExpenseType = value! }, "occurrenceType" => detail with { OccurrenceType = value! },
            _ => detail with { Name = value! }
        };
        Reject(Input(Group(detail)), "groups[0].details[0]." + field);
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00")]
    [InlineData("2024-02-29T23:59:59")]
    [InlineData("9999-12-31T23:59:59")]
    public void LocalSecondPrecisionDateLimitsAreAccepted(string at) => Assert.Equal(110, ReceiptCalculator.Calculate(Input(Group(Detail(100))) with { TransactionAt = at }).TotalAmount);

    [Theory]
    [InlineData("shopName")]
    [InlineData("shopLocation")]
    [InlineData("name")]
    public void Utf16LengthIsCheckedWithoutTrimmingOrChangingSource(string field)
    {
        var exact = string.Concat(Enumerable.Repeat("😀", 50));
        ReceiptCalculationInput With(string text) => field switch
        {
            "shopName" => Input(Group(Detail(100))) with { ShopName = text },
            "shopLocation" => Input(Group(Detail(100))) with { ShopLocation = text },
            _ => Input(Group(Detail(100) with { Name = text }))
        };
        Assert.Equal(110, ReceiptCalculator.Calculate(With(exact)).TotalAmount);
        Reject(With(exact + "a"), field == "name" ? "groups[0].details[0].name" : field);
        var spaced = With(" 店舗 "); var before = JsonSerializer.Serialize(spaced);
        ReceiptCalculator.Calculate(spaced); Assert.Equal(before, JsonSerializer.Serialize(spaced));
    }

    [Fact]
    public void NoUnapprovedTrimOrMinimumDetailNameRuleIsAdded()
    {
        Assert.Equal(110, ReceiptCalculator.Calculate(Input(Group(Detail(100) with { Name = "" })) with { ShopName = "   ", ShopLocation = "" }).TotalAmount);
    }

    [Fact]
    public void EmptyNullAndDuplicateGroupsAreRejectedAtTheirInputPaths()
    {
        Reject(Input(), "groups"); Reject(Input() with { Groups = null! }, "groups");
        Reject(Input(new ReceiptCalculationGroup(Category, [])), "groups[0].details");
        Reject(Input(new ReceiptCalculationGroup(Category, null!)), "groups[0].details");
        Reject(Input((ReceiptCalculationGroup)null!), "groups[0]"); Reject(Input(Group((ReceiptCalculationDetail)null!)), "groups[0].details[0]");
        Reject(Input(new ReceiptCalculationGroup(Guid.Empty, [Detail(100)])), "groups[0].categoryId");
        Reject(Input(Group(Detail(100)), Group(Detail(100))), "groups[1].categoryId");
    }

    [Fact]
    public void CyclesRequireARegularDetailButRegularDoesNotRequireACycle()
    {
        var input = Input(Group(Detail(100)));
        Reject(input with { RecurrenceUnit = "Month" }, "recurrenceUnit");
        Reject(input with { RecurrenceInterval = 1, RecurrenceUnit = "Month" }, "groups");
        foreach (var unit in new[] { "Week", "Month", "Year" })
        foreach (var interval in new[] { 1L, int.MaxValue })
        {
            var regular = Input(Group(Detail(100) with { OccurrenceType = "Regular" }));
            Assert.Equal(110, ReceiptCalculator.Calculate(regular).TotalAmount);
            Assert.Equal(110, ReceiptCalculator.Calculate(regular with { RecurrenceInterval = interval, RecurrenceUnit = unit }).TotalAmount);
        }
    }

    [Theory]
    [InlineData("Fixed")]
    [InlineData("Variable")]
    [InlineData("Excluded")]
    public void ExpenseCategoryChoicesDependOnCategoryAndNeverOnMoneySign(string expense)
    {
        foreach (var price in new[] { 100, -100 })
        {
            var input = Input(Group(Detail(price) with { ExpenseType = expense }));
            Assert.Equal(price * 11 / 10, ReceiptValidation.ValidateForSave(input, Masters(IncomeExpenseKind.Expense)).TotalAmount);
            if (expense == "Excluded") Assert.Equal(price * 11 / 10, ReceiptValidation.ValidateForSave(input, Masters(IncomeExpenseKind.Income)).TotalAmount);
            else Assert.Equal("groups[0].details[0].expenseType", Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input, Masters(IncomeExpenseKind.Income))).Field);
        }
    }

    [Theory]
    [InlineData("2026-04-01T00:00:00", true)]
    [InlineData("2026-09-30T23:59:59", true)]
    [InlineData("2026-03-31T23:59:59", false)]
    [InlineData("2026-10-01T00:00:00", false)]
    public void TaxHistoryPeriodBoundariesAreInclusiveAndUseTransactionDay(string at, bool valid)
    {
        var input = Input(Group(Detail(100))) with { TransactionAt = at };
        var masters = Masters(IncomeExpenseKind.Expense) with { TaxPeriods = [new(new(2026, 4, 1), new(2026, 9, 30), 10m)] };
        if (valid) Assert.Equal(110, ReceiptValidation.ValidateForSave(input, masters).TotalAmount);
        else Assert.Equal("groups[0].details[0].taxRate", Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input, masters)).Field);
    }

    [Fact]
    public void MissingHistoryNeverFallsBackAndExemptIsNotTaxableZero()
    {
        var masters = Masters(IncomeExpenseKind.Expense) with { TaxPeriods = [] };
        Assert.Equal(100, ReceiptValidation.ValidateForSave(Input(Group(Detail(100, null))), masters).TotalAmount);
        Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(Input(Group(Detail(100, "0"))), masters));
        Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(Input(Group(Detail(100))), masters));
        masters = masters with { TaxPeriods = [new(new(2026, 1, 1), null, 0m)] };
        Assert.Equal(100, ReceiptValidation.ValidateForSave(Input(Group(Detail(100, "0"))), masters).TotalAmount);
    }

    [Fact]
    public void GapsBetweenHistoriesDoNotUseThePreviousOrNextRate()
    {
        var input = Input(Group(Detail(100))) with { TransactionAt = "2026-02-15T12:00:00" };
        var masters = Masters(IncomeExpenseKind.Expense) with { TaxPeriods = [
            new(new(2026, 1, 1), new(2026, 1, 31), 10m), new(new(2026, 3, 1), null, 10m)] };
        Assert.Equal("groups[0].details[0].taxRate", Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input, masters)).Field);
    }

    [Fact]
    public void QuantityDisplayOrderAndSignedDiscountLimitsAreAccepted()
    {
        var detail = Detail(int.MaxValue, null) with { UnitDiscountAmount = int.MaxValue, Quantity = int.MaxValue, DisplayOrder = int.MaxValue, DiscountRate = "100" };
        Assert.Equal(0, ReceiptCalculator.Calculate(Input(Group(detail))).TotalAmount);
        Assert.Equal(0, ReceiptCalculator.Calculate(Input(Group(detail with { UnitPrice = int.MinValue, UnitDiscountAmount = int.MinValue }))).TotalAmount);
        Assert.Equal(110, ReceiptCalculator.Calculate(Input(Group(Detail(100, null) with { UnitDiscountAmount = -10 }))).TotalAmount);
    }

    [Fact]
    public void NumericRateEqualityAndCultureAreIndependentOfTaxCategoryCodes()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("fr-FR");
            var masters = Masters(IncomeExpenseKind.Expense) with { TaxPeriods = [new(new(2026, 1, 1), null, 8m), new(new(2026, 1, 1), null, 8.0m)] };
            var result = ReceiptValidation.ValidateForSave(Input(Group(Detail(100, "08.00"), Detail(100, "8.0"))), masters);
            Assert.Single(result.TaxSubtotals); Assert.Equal(216, result.TotalAmount);
            // 最小桁のDecimalも入力として受理し、旧版と同じDecimal演算順で計算する。
            Assert.Equal(1, ReceiptCalculator.Calculate(Input(Group(Detail(1) with { DiscountRate = "0.0000000000000000000000000001" }))).TotalAmount);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RemovedRateRejectsUnchangedInputWithoutRewritingItsRate()
    {
        var input = Input(Group(Detail(100))); var before = JsonSerializer.Serialize(input);
        var masters = Masters(IncomeExpenseKind.Expense) with { TaxPeriods = [new(new(2026, 1, 1), null, 8m)] };
        Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input, masters));
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }

    [Fact]
    public void FirstErrorFollowsParentGroupAndDetailOrderIncludingMasterChecks()
    {
        var invalid = Detail(100) with { Quantity = 0 };
        var input = Input(Group(Detail(100)), GroupB(invalid));
        var masters = Masters(IncomeExpenseKind.Expense) with { CategoryIncomeExpenses = new Dictionary<Guid, IncomeExpenseKind>() };
        Assert.Equal("groups[0].categoryId", Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input, masters)).Field);
        Reject(input with { TaxRoundingMode = "bad" }, "taxRoundingMode");
        Reject(Input(Group(invalid, Detail(100) with { DiscountRate = "invalid" })), "groups[0].details[0].quantity");
    }

    private static ReceiptMasterData Masters(IncomeExpenseKind kind) => new(new Dictionary<Guid, IncomeExpenseKind> { [Category] = kind }, [new(new(2026, 1, 1), null, 10m)]);
    private static void Reject(ReceiptCalculationInput input, string field)
    {
        var before = JsonSerializer.Serialize(input);
        Assert.Equal(field, Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(input)).Field);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }
}
