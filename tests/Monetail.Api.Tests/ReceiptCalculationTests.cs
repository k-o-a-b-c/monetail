using Monetail.Api.Features.Monetail.Receipts;
using Xunit;
using static Monetail.Api.Tests.ReceiptCalculationTestData;

namespace Monetail.Api.Tests;

public sealed class ReceiptCalculationTests
{
    [Fact]
    public void MixedRatesAggregateAcrossCategoriesWithoutAddingReferenceAmounts()
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(19), Detail(19), Detail(19, "8")), GroupB(Detail(19))));
        Assert.Equal(82, result.TotalAmount);
        Assert.Equal(61, result.Groups[0].TotalAmount); Assert.Equal(20, result.Groups[1].TotalAmount);
        Assert.Equal(60, result.Groups[0].Details.Sum(row => row.TotalAmount));
        Assert.Equal(2, result.TaxSubtotals.Count);
    }

    [Fact]
    public void NegativeTaxUsesDecimalAndRoundsAwayFromZeroAtHalf()
    {
        foreach (var mode in new[] { "floor", "half_up" })
        {
            var result = ReceiptCalculator.Calculate(Input(Group(Detail(-100))) with { TaxRoundingMode = mode });
            Assert.Equal(-110, result.TotalAmount); Assert.Equal(-110, result.IncomeAmount);
            Assert.Equal(0, result.ExpenseAmount);
            Assert.Equal(-6, ReceiptCalculator.Calculate(Input(Group(Detail(-5))) with { TaxRoundingMode = mode }).TotalAmount);
        }
    }

    [Fact]
    public void DecimalDiscountAppliesUnitDiscountThenQuantityThenCeiling()
    {
        var detail = Detail(1503) with { UnitDiscountAmount = 3, Quantity = 2, DiscountRate = "1.1" };
        var input = Input(Group(detail));
        var floor = ReceiptCalculator.Calculate(input);
        Assert.Equal(2967m, floor.Groups[0].Details[0].NetAmount); Assert.Equal(3263, floor.TotalAmount);
        Assert.Equal(3264, ReceiptCalculator.Calculate(input with { TaxRoundingMode = "half_up" }).TotalAmount);
        Assert.Equal("1.1", detail.DiscountRate); Assert.Equal(1503, detail.UnitPrice);
    }

    [Theory]
    [InlineData(100, "2.5", 97)]
    [InlineData(-100, "2.5", -98)]
    [InlineData(3000, "1.1", 2967)]
    [InlineData(-3000, "1.1", -2967)]
    [InlineData(1500, "2.2", 1467)]
    [InlineData(3000, "0", 3000)]
    [InlineData(3000, "100", 0)]
    [InlineData(3000, "1.001", 2969)]
    [InlineData(1, "0.1", 0)]
    [InlineData(-1, "0.1", -1)]
    public void DiscountFixedExpectations(long price, string rate, int expected)
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(price) with { DiscountRate = rate })) with { TaxInclusionMode = 0 });
        Assert.Equal(expected, result.TotalAmount); Assert.Equal(expected, result.Groups[0].Details[0].NetAmount);
    }

    [Theory]
    [InlineData(0, "floor", 5, 5)]
    [InlineData(0, "half_up", -5, -5)]
    [InlineData(1, "floor", 5, 5)]
    [InlineData(1, "half_up", 5, 6)]
    [InlineData(1, "floor", -5, -6)]
    [InlineData(1, "half_up", -5, -6)]
    [InlineData(1, "floor", 19, 20)]
    [InlineData(1, "half_up", 19, 21)]
    [InlineData(1, "floor", -50, -55)]
    [InlineData(1, "half_up", -50, -55)]
    public void TaxRoundingFixedExpectations(int inclusion, string rounding, int price, int expected)
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(price))) with { TaxInclusionMode = inclusion, TaxRoundingMode = rounding });
        Assert.Equal(expected, result.TotalAmount); Assert.Equal(expected, result.Groups[0].TotalAmount);
        Assert.Equal(expected, result.Groups[0].Details[0].TotalAmount);
    }

    [Theory]
    [InlineData("floor", 4, -2, 5)]
    [InlineData("half_up", 4, -1, 6)]
    public void SignedTotalsDoNotReconstructAccountingTotal(string rounding, int total, int income, int expense)
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(5), Detail(-1))) with { TaxRoundingMode = rounding });
        Assert.Equal(total, result.TotalAmount); Assert.Equal(income, result.IncomeAmount); Assert.Equal(expense, result.ExpenseAmount);
        Assert.NotEqual(result.TotalAmount, result.IncomeAmount + result.ExpenseAmount);
    }

    [Theory]
    [InlineData("floor", 41, 40)]
    [InlineData("half_up", 42, 42)]
    public void SameRateAcrossDifferentGroupsRoundsOnlyAfterReceiptAggregation(string mode, int total, int references)
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(19)), GroupB(Detail(19, "010.00"))) with { TaxRoundingMode = mode });
        Assert.Equal(total, result.TotalAmount); Assert.Equal(references, result.Groups.Sum(row => row.TotalAmount));
        Assert.Single(result.TaxSubtotals);
    }

    [Fact]
    public void HalfUpGroupAndDetailReferenceSumCanExceedTotal()
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(15)), GroupB(Detail(15))) with { TaxRoundingMode = "half_up" });
        Assert.Equal(33, result.TotalAmount); Assert.Equal(34, result.Groups.Sum(row => row.TotalAmount));
    }

    [Theory]
    [InlineData(0, "floor")]
    [InlineData(0, "half_up")]
    [InlineData(1, "floor")]
    [InlineData(1, "half_up")]
    public void ExemptAndTaxableZeroRemainDistinctWithEqualFinancialEffect(int inclusion, string rounding)
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(19, null), Detail(-1, "0"), Detail(5))) with { TaxInclusionMode = inclusion, TaxRoundingMode = rounding });
        Assert.Equal(3, result.TaxSubtotals.Count);
        Assert.Equal(19m, result.TaxSubtotals.Single(row => row.TaxRate is null).TotalAmount);
        Assert.Equal(-1m, result.TaxSubtotals.Single(row => row.TaxRate == 0m).TotalAmount);
        Assert.Equal(inclusion == 1 && rounding == "half_up" ? 24 : 23, result.TotalAmount);
    }

    [Theory]
    [InlineData(2147483647)]
    [InlineData(-2147483648)]
    public void ExactInt32LimitsAreAccepted(long price)
    {
        foreach (var mode in new[] { "floor", "half_up" })
        foreach (var inclusion in new[] { 0, 1 })
        foreach (var tax in new string?[] { null, "0" })
            Assert.Equal(price, ReceiptCalculator.Calculate(Input(Group(Detail(price, tax))) with { TaxInclusionMode = inclusion, TaxRoundingMode = mode }).TotalAmount);
    }

    [Fact]
    public void WideIntermediateAmountWithFullDiscountIsAccepted()
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(2_000_000_000) with { Quantity = 2, DiscountRate = "100" })));
        Assert.Equal(0, result.TotalAmount); Assert.Equal(0m, result.Groups[0].Details[0].NetAmount);
    }

    [Theory]
    [InlineData(2_000_000_000, 2, 0, "0")]
    [InlineData(-2_000_000_000, 2, 0, "0")]
    [InlineData(2147483647, 1, -1, "0")]
    [InlineData(2_000_000_000, 1, 0, "0")]
    public void IndividualFinalAmountsOutsideInt32AreRejected(long price, long quantity, long discount, string rate)
    {
        Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(Input(Group(Detail(price) with {
            Quantity = quantity, UnitDiscountAmount = discount, DiscountRate = rate }))));
    }

    [Fact]
    public void CancellationCannotHideSignedGroupOrDetailOverflow()
    {
        foreach (var sign in new[] { 1, -1 })
        {
            Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(Input(
                Group(Detail(sign * 1_500_000_000, null), Detail(sign * 1_500_000_000, null)), GroupB(Detail(-sign * 2_000_000_000, null)))));
            Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(Input(
                Group(Detail(sign * 1_500_000_000, null), Detail(sign * 1_500_000_000, null), Detail(-sign * 2_000_000_000, null)))));
        }
    }

    [Fact]
    public void ReceiptOverflowIsRejectedEvenWhenEachGroupFits()
    {
        Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(Input(Group(Detail(1_500_000_000, null)), GroupB(Detail(1_500_000_000, null)))));
    }

    [Theory]
    [InlineData("floor", 200)]
    [InlineData("half_up", 2)]
    public void DecimalIntermediateOverflowIsAValidationFailureWithoutChangingInput(string rounding, int price)
    {
        var input = Input(Group(Detail(price, "79228162514264337593543950335"))) with { TaxRoundingMode = rounding };
        Assert.Throws<ReceiptValidationException>(() => ReceiptCalculator.Calculate(input));
        Assert.Equal("79228162514264337593543950335", input.Groups[0].Details[0].TaxRate);
    }

    [Fact]
    public void DecimalFractionalTaxAndZeroAmountHaveFixedExpectations()
    {
        var result = ReceiptCalculator.Calculate(Input(Group(Detail(100, "8.25"), Detail(0, "10"))));
        Assert.Equal(108, result.TotalAmount);
        Assert.Equal(108, ReceiptCalculator.Calculate(Input(Group(Detail(100, "8.25"))) with { TaxRoundingMode = "half_up" }).TotalAmount);
        Assert.Equal(0, result.IncomeAmount);
    }
}

internal static class ReceiptCalculationTestData
{
    internal static readonly Guid Category = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid OtherCategory = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static ReceiptCalculationDetail Detail(long price, string? tax = "10") => new("明細", price, 1, 0, "0", tax, "Variable", "Irregular");
    internal static ReceiptCalculationGroup Group(params ReceiptCalculationDetail[] details) => new(Category, details);
    internal static ReceiptCalculationGroup GroupB(params ReceiptCalculationDetail[] details) => new(OtherCategory, details);
    internal static ReceiptCalculationInput Input(params ReceiptCalculationGroup[] groups) => new("2026-11-10T12:34:56", " 店舗 ", null, 1, "floor", null, null, groups);
}
