using System.Globalization;
using System.Text.Json;
using Monetail.Api.Features.Monetail.Receipts;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>
/// CIで旧VB実コードをnet8で動かした出力と、新C#実コードの結果を同一JSONで比較する。
/// 通常のAPI単独テスト時は共有fixtureの解析だけを検証する。
/// </summary>
public sealed class ReceiptParityTests
{
    [Fact]
    public void OldVbAndNewApiCalculateTheSameValues()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "receipt-parity-cases.json")));
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.True(cases.Length >= 20, "差分計算のケース数が不足しています。");
        Assert.Equal(cases.Length, cases.Select(x => x.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());

        var legacyPath = Environment.GetEnvironmentVariable("MONETAIL_LEGACY_PARITY_RESULTS");
        if (string.IsNullOrWhiteSpace(legacyPath))
            return; // CIの専用jobで旧VB出力を必須にして実照合する。

        using var legacyFile = JsonDocument.Parse(File.ReadAllText(legacyPath));
        Assert.Equal(1, legacyFile.RootElement.GetProperty("formatVersion").GetInt32());
        var legacyCases = legacyFile.RootElement.GetProperty("cases").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetString()!, x => x, StringComparer.Ordinal);
        Assert.Equal(cases.Length, legacyCases.Count);

        foreach (var testCase in cases)
        {
            var id = testCase.GetProperty("id").GetString()!;
            Assert.True(legacyCases.TryGetValue(id, out var legacy), $"旧VBの結果に{id}がありません。");
            var groups = testCase.GetProperty("groups").EnumerateArray()
                .Select((g, index) => new ReceiptCalculationGroup(
                    Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
                    g.GetProperty("details").EnumerateArray().Select((d, detailIndex) =>
                        new ReceiptCalculationDetail(
                            "item", d.GetProperty("price").GetInt64(),
                            d.GetProperty("quantity").GetInt64(),
                            d.GetProperty("discount").GetInt64(),
                            d.GetProperty("discountRate").GetString()!,
                            d.GetProperty("taxable").GetBoolean()
                                ? g.GetProperty("rate").GetInt32().ToString(CultureInfo.InvariantCulture)
                                : null,
                            "Variable", "Irregular", detailIndex)).ToArray()))
                .ToArray();
            var input = new ReceiptCalculationInput("2026-11-12T12:00:00", "test", null,
                testCase.GetProperty("inclusion").GetInt32(), testCase.GetProperty("rounding").GetString()!,
                null, null, groups);
            var actual = ReceiptCalculator.Calculate(input);

            Assert.Equal(legacy.GetProperty("totalAmount").GetInt32(), actual.TotalAmount);
            Assert.Equal(legacy.GetProperty("incomeAmount").GetInt32(), actual.IncomeAmount);
            Assert.Equal(legacy.GetProperty("expenseAmount").GetInt32(), actual.ExpenseAmount);
            var legacyGroups = legacy.GetProperty("groups").EnumerateArray().ToArray();
            Assert.Equal(legacyGroups.Length, actual.Groups.Count);
            for (var gi = 0; gi < groups.Length; gi++)
            {
                Assert.Equal(legacyGroups[gi].GetProperty("referenceAmount").GetInt32(), actual.Groups[gi].TotalAmount);
                var legacyDetails = legacyGroups[gi].GetProperty("details").EnumerateArray().ToArray();
                Assert.Equal(legacyDetails.Length, actual.Groups[gi].Details.Count);
                for (var di = 0; di < legacyDetails.Length; di++)
                {
                    Assert.Equal(legacyDetails[di].GetProperty("netAmount").GetDecimal(), actual.Groups[gi].Details[di].NetAmount);
                    Assert.Equal(legacyDetails[di].GetProperty("referenceAmount").GetInt32(), actual.Groups[gi].Details[di].TotalAmount);
                }
            }
        }
    }
}
