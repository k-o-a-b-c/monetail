using System.Globalization;
using System.Text.Json;
using Monetail.Calculations.Receipts;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ReceiptCalculationTests
{
    private static readonly Guid Category = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTime At = new(2026, 11, 10, 12, 34, 56);
    private static ReceiptDetailSnapshot Detail(int price, decimal? rate = 10m) => new(Guid.NewGuid(), 0, "明細", price, 1, 0, 0, rate, "Variable", "Irregular", null, []);
    private static ReceiptSnapshot Snapshot(params ReceiptDetailSnapshot[] rows) => new(At, " 店舗 ", null, Guid.NewGuid(), 1, "floor", null, null, null, [new(Guid.NewGuid(), Category, null, rows)]);

    [Fact]
    public void ClientUsesTheSameCalculationAssemblyForEveryLegacyParityInput()
    {
        var shared = typeof(ReceiptCalculator).Assembly;
        Assert.Equal(shared, typeof(ReceiptCalculations).GetMethod(nameof(ReceiptCalculations.FromSnapshot))!.ReturnType.Assembly);
        Assert.Contains(typeof(ReceiptCalculations).Assembly.GetReferencedAssemblies(), a => a.Name == shared.GetName().Name);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "receipt-parity-cases.json")));
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(24, cases.Length);
        foreach (var row in cases)
        {
            var groups = row.GetProperty("groups").EnumerateArray().Select((g, i) => new ReceiptGroupSnapshot(null,
                Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"), null, g.GetProperty("details").EnumerateArray().Select((d, j) =>
                    Detail(d.GetProperty("price").GetInt32(), d.GetProperty("taxable").GetBoolean() ? g.GetProperty("rate").GetDecimal() : null) with {
                        Name = "item", DisplayOrder = j, Quantity = d.GetProperty("quantity").GetInt32(), UnitDiscountAmount = d.GetProperty("discount").GetInt32(),
                        DiscountRate = decimal.Parse(d.GetProperty("discountRate").GetString()!, CultureInfo.InvariantCulture) }).ToArray())).ToArray();
            var snapshot = Snapshot() with { TransactionAt = new(2026, 11, 12, 12, 0, 0), ShopName = "test", TaxInclusionMode = row.GetProperty("inclusion").GetInt32(), TaxRoundingMode = row.GetProperty("rounding").GetString()!, Groups = groups };
            // APIの旧VB照合と同じfixtureから期待入力を直接組み立て、変換漏れも検出する。
            var input = new ReceiptCalculationInput("2026-11-12T12:00:00", "test", null,
                row.GetProperty("inclusion").GetInt32(), row.GetProperty("rounding").GetString()!, null, null,
                row.GetProperty("groups").EnumerateArray().Select((g, i) => new ReceiptCalculationGroup(
                    Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"), g.GetProperty("details").EnumerateArray().Select((d, j) =>
                        new ReceiptCalculationDetail("item", d.GetProperty("price").GetInt64(), d.GetProperty("quantity").GetInt64(),
                            d.GetProperty("discount").GetInt64(), d.GetProperty("discountRate").GetString()!,
                            d.GetProperty("taxable").GetBoolean() ? g.GetProperty("rate").GetInt32().ToString(CultureInfo.InvariantCulture) : null,
                            "Variable", "Irregular", j)).ToArray())).ToArray());
            Assert.Equal(JsonSerializer.Serialize(input), JsonSerializer.Serialize(ReceiptCalculations.FromSnapshot(snapshot)));
            var before = JsonSerializer.Serialize(snapshot);
            var expected = ReceiptCalculator.Calculate(input); var actual = ReceiptCalculations.Calculate(snapshot);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.Equal(shared, actual.GetType().Assembly); Assert.Equal(before, JsonSerializer.Serialize(snapshot));
        }
    }

    [Theory]
    [InlineData("floor", 4, -2, 5)] [InlineData("half_up", 4, -1, 6)]
    public void SignedTotalsAndReferenceAmountsKeepFixedExpectations(string mode, int total, int income, int expense)
    {
        var result = ReceiptCalculations.Calculate(Snapshot(Detail(5), Detail(-1)) with { TaxRoundingMode = mode });
        Assert.Equal(total, result.TotalAmount); Assert.Equal(income, result.IncomeAmount); Assert.Equal(expense, result.ExpenseAmount);
        Assert.NotEqual(result.TotalAmount, result.Groups[0].Details.Sum(d => d.TotalAmount));
    }
    [Fact]
    public void NullZeroAndRatesAcrossCategoriesKeepSeparateBucketsAndAccountingGranularity()
    {
        var snapshot = Snapshot(Detail(19), Detail(1, null), Detail(1, 0));
        snapshot = snapshot with { Groups = [snapshot.Groups[0], new(null, Guid.NewGuid(), null, [Detail(19)])] };
        var result = ReceiptCalculations.Calculate(snapshot);
        Assert.Equal(43, result.TotalAmount); Assert.Equal(42, result.Groups.Sum(g => g.TotalAmount));
        Assert.Equal(3, result.TaxSubtotals.Count); Assert.Contains(result.TaxSubtotals, s => s.TaxRate is null); Assert.Contains(result.TaxSubtotals, s => s.TaxRate == 0m);
    }
    [Fact]
    public void IntermediateInt32OverflowCanBecomeZeroButFinalAndDecimalOverflowAreRejected()
    {
        Assert.Equal(0, ReceiptCalculations.Calculate(Snapshot(Detail(int.MaxValue, null) with { Quantity = int.MaxValue, DiscountRate = 100m })).TotalAmount);
        var overflow = Assert.Throws<ReceiptValidationException>(() => ReceiptCalculations.Calculate(Snapshot(Detail(int.MaxValue))));
        Assert.Equal("groups[0].details[0]", overflow.Field); Assert.Equal("金額がInt32の範囲を超えています。", overflow.Message);
        var decimalOverflow = Assert.Throws<ReceiptValidationException>(() => ReceiptCalculations.Calculate(Snapshot(Detail(int.MaxValue, decimal.MaxValue))));
        Assert.Equal("groups[0].details[0]", decimalOverflow.Field); Assert.Equal("金額の計算が扱える範囲を超えています。", decimalOverflow.Message);
    }
    [Theory]
    [InlineData("date", "transactionAt", "有効なローカル日時を秒まで指定してください。")]
    [InlineData("quantity", "groups[0].details[0].quantity", "数量は1以上を指定してください。")]
    [InlineData("discount", "groups[0].details[0].discountRate", "値引率は0〜100を指定してください。")]
    public void InvalidInputKeepsSharedFieldAndMessageWithoutChangingSnapshot(string kind, string field, string message)
    {
        var row = Detail(1); if (kind == "quantity") row = row with { Quantity = 0, DiscountRate = 101m };
        if (kind == "discount") row = row with { DiscountRate = 101m };
        var snapshot = Snapshot(row); if (kind == "date") snapshot = snapshot with { TransactionAt = At.AddTicks(1) };
        var before = JsonSerializer.Serialize(snapshot);
        var exception = Assert.Throws<ReceiptValidationException>(() => ReceiptCalculations.Calculate(snapshot));
        Assert.Equal(field, exception.Field); Assert.Equal(message, exception.Message); Assert.Equal(before, JsonSerializer.Serialize(snapshot));
    }
}
