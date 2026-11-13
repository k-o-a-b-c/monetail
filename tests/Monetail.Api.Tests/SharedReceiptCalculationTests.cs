using System.Text.Json;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;
using Xunit;
using Shared = Monetail.Calculations.Receipts;
using static Monetail.Api.Tests.ReceiptCalculationTestData;

namespace Monetail.Api.Tests;

public sealed class SharedReceiptCalculationTests
{
    [Fact]
    public void ApiAdapterUsesSharedModelsAndDependencyFreeCalculationAssembly()
    {
        var assembly = typeof(Shared.ReceiptCalculator).Assembly;
        Assert.Equal("Monetail.Calculations", assembly.GetName().Name);
        var method = typeof(ReceiptCalculator).GetMethod(nameof(ReceiptCalculator.Calculate))!;
        Assert.Equal(assembly, method.ReturnType.Assembly);
        Assert.Equal(assembly, method.GetParameters()[0].ParameterType.Assembly);
        Assert.Contains(typeof(Program).Assembly.GetReferencedAssemblies(), a => a.Name == assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Monetail.") ||
            a.Name.StartsWith("Microsoft.AspNetCore") || a.Name.StartsWith("Microsoft.EntityFrameworkCore") ||
            a.Name.Contains("Sqlite") || a.Name.Contains("WinUI"));
        foreach (var mode in new[] { "floor", "half_up" })
        {
            var input = Input(Group(Detail(19), Detail(-5, null)), GroupB(Detail(19, "0"))) with { TaxRoundingMode = mode };
            Assert.Equal(JsonSerializer.Serialize(Shared.ReceiptCalculator.Calculate(input)), JsonSerializer.Serialize(ReceiptCalculator.Calculate(input)));
        }
    }

    [Theory]
    [InlineData("header", "transactionAt", "有効なローカル日時を秒まで指定してください。")]
    [InlineData("category", "groups[0].categoryId", "科目を確認してください。")]
    [InlineData("quantity", "groups[0].details[0].quantity", "数量は1以上を指定してください。")]
    [InlineData("tax", "groups[0].details[0].taxRate", "取引日に適用される税率を選択してください。")]
    [InlineData("income", "groups[0].details[0].expenseType", "収入科目の費用区分は対象外を指定してください。")]
    [InlineData("expense", "groups[0].details[0].expenseType", "費用区分を指定してください。")]
    public void MasterAndBasicChecksKeepOriginalErrorPriorityAndMessages(string kind, string field, string message)
    {
        var line = Detail(100) with { OccurrenceType = "invalid" };
        if (kind is "quantity" or "tax") line = line with { TaxRate = "9", ExpenseType = "invalid" };
        if (kind == "quantity") line = line with { Quantity = 0 };
        if (kind == "expense") line = line with { ExpenseType = "invalid" };
        var input = Input(Group(line));
        var categories = new Dictionary<Guid, IncomeExpenseKind> { [Category] = kind == "income" ? IncomeExpenseKind.Income : IncomeExpenseKind.Expense };
        if (kind is "header" or "category") { categories.Clear(); input = input with { Groups = [new(Category, [])] }; }
        if (kind == "header") input = input with { TransactionAt = "invalid" };
        var before = JsonSerializer.Serialize(input);
        var exception = Assert.Throws<ReceiptValidationException>(() => ReceiptValidation.ValidateForSave(input,
            new(categories, [new(new(2026, 1, 1), null, 10m)])));
        Assert.Equal(field, exception.Field); Assert.Equal(message, exception.Message);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }
}
