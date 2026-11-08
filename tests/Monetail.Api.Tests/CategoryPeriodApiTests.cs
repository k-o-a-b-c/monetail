using System.Text.Json;
using Xunit;

namespace Monetail.Api.Tests;

public sealed partial class CategoryApiTests
{
    [Theory]
    [InlineData(null,null)] [InlineData("2026-04-01",null)] [InlineData(null,"2026-09-30")]
    [InlineData("2026-04-01","2026-09-30")] [InlineData("2026-04-01","2026-04-01")]
    [InlineData("2024-02-29","2024-02-29")] [InlineData("0001-01-01","9999-12-31")]
    public async Task CategoryCreationStoresNullableInclusiveDateBoundsAndReturnsAllPeriods(string? from,string? to)
    {
        await using var f=await Fixture.Create();
        var created=await f.Call("POST",new {requestId=Guid.NewGuid(),name="期間科目",incomeExpense="Expense",taxCategoryCode="Standard",validFrom=from,validTo=to},201);
        var row=Row(created); Assert.Equal(from,row.GetProperty("validFrom").GetString()); Assert.Equal(to,row.GetProperty("validTo").GetString());
        Assert.False(row.TryGetProperty("isEnabled",out _));
        Assert.Equal(row.GetRawText(),Row(await f.Call("GET")).GetRawText());
        foreach(var date in new[]{"2026-03-31","2026-04-01","2026-09-30","2026-10-01"})
        {
            var expected=(from is null || string.CompareOrdinal(from,date)<=0) && (to is null || string.CompareOrdinal(date,to)<=0);
            Assert.Equal(expected?1L:0L,f.App.Database.Scalar($"SELECT COUNT(*) FROM ledger_categories WHERE (valid_from IS NULL OR valid_from<='{date}') AND (valid_to IS NULL OR valid_to>='{date}')"));
        }
    }

    [Fact]
    public async Task UsedCategoryCanChangeIncomeAndPeriodWhileOmittedFieldsAndOrderVersionsArePreserved()
    {
        await using var f=await Fixture.Create();
        var row=Row(await f.Call("POST",new{requestId=Guid.NewGuid(),name="使用済み",incomeExpense="Expense",taxCategoryCode="Standard",validFrom="2026-04-01",validTo="2026-09-30"},201));
        f.App.Database.Execute($"CREATE TABLE category_use_probe(ledger_id TEXT,category_id TEXT,amount INTEGER,applied_tax TEXT,rate TEXT,FOREIGN KEY(ledger_id,category_id) REFERENCES ledger_categories(ledger_id,category_id) ON DELETE RESTRICT); INSERT INTO category_use_probe VALUES('{f.Ledger}','{Id(row)}',-100,'Standard','10')");
        var saved=f.App.Database.Snapshot(new[]{"category_use_probe"}); var order=f.Order(); var header=f.Header();
        var edited=Row(await f.Call("PATCH",new{revision=Revision(row),name="変更名"},suffix:Path(row)));
        Assert.Equal("2026-04-01",edited.GetProperty("validFrom").GetString()); Assert.Equal("2026-09-30",edited.GetProperty("validTo").GetString());
        edited=Row(await f.Call("PATCH",new{revision=Revision(edited),incomeExpense="Income",validFrom=(string?)null},suffix:Path(row)));
        Assert.Equal("Income",edited.GetProperty("incomeExpense").GetString()); Assert.Equal(JsonValueKind.Null,edited.GetProperty("validFrom").ValueKind);
        Assert.Equal("2026-09-30",edited.GetProperty("validTo").GetString());
        edited=Row(await f.Call("PATCH",new{revision=Revision(edited),validTo=(string?)null},suffix:Path(row)));
        Assert.Equal(JsonValueKind.Null,edited.GetProperty("validTo").ValueKind);
        var before=f.Snapshot();
        await f.Call("PATCH",new{revision=Revision(edited),incomeExpense="Income",validFrom=(string?)null,validTo=(string?)null},suffix:Path(row));
        Assert.Equal(before,f.Snapshot()); Assert.Equal(order,f.Order()); Assert.Equal(header,f.Header()); Assert.Equal(saved,f.App.Database.Snapshot(new[]{"category_use_probe"}));
        Assert.Equal("category_referenced",Code(await f.Call("DELETE",new{revision=Revision(edited)},409,Path(row))));
    }

    [Theory]
    [InlineData("2026-02-29")] [InlineData("2026/04/01")] [InlineData("2026-4-1")]
    [InlineData("2026-04-01T00:00:00")] [InlineData("0000-01-01")] [InlineData("")]
    public async Task CategoryInvalidDatesAreRejectedForCreateAndPatchWithoutWrites(string date)
    {
        await using var f=await Fixture.Create(); var row=await f.Category(); var before=f.Snapshot();
        await f.Call("POST",new{requestId=Guid.NewGuid(),name="不正",incomeExpense="Expense",taxCategoryCode="Standard",validFrom=date},400);
        await f.Call("PATCH",new{revision=Revision(row),validTo=date},400,Path(row)); Assert.Equal(before,f.Snapshot());
    }

    [Fact]
    public async Task CategoryPeriodValidationMergesOmittedBoundsAndDoesNotAdvanceVersionsOnRejection()
    {
        await using var f=await Fixture.Create();
        var row=Row(await f.Call("POST",new{requestId=Guid.NewGuid(),name="期間",incomeExpense="Income",taxCategoryCode="Exempt",validFrom="2026-04-01",validTo="2026-09-30"},201));
        var before=f.Snapshot();
        await f.Call("PATCH",new{revision=Revision(row),validFrom="2026-10-01"},400,Path(row));
        await f.Call("PATCH",new{revision=Revision(row),validTo="2026-03-31"},400,Path(row));
        await f.Call("POST",new{requestId=Guid.NewGuid(),name="逆転",incomeExpense="Income",taxCategoryCode="Exempt",validFrom="2026-10-01",validTo="2026-09-30"},400);
        Assert.Equal(before,f.Snapshot());
    }

    [Fact]
    public async Task CreationReplayComparesNormalizedPeriodsAndTreatsOmissionAndNullEqually()
    {
        await using var f=await Fixture.Create(); var request=Guid.NewGuid();
        var input=new{requestId=request,name="期間",incomeExpense="Expense",taxCategoryCode="Standard",validFrom="2026-04-01",validTo="2026-09-30"};
        var created=await f.Call("POST",input,201); var before=f.Snapshot();
        Assert.Equal(created.GetRawText(),(await f.Call("POST",input)).GetRawText());
        Assert.Equal("request_conflict",Code(await f.Call("POST",new{requestId=request,name="期間",incomeExpense="Expense",taxCategoryCode="Standard",validFrom="2026-04-01",validTo="2026-10-01"},409)));
        Assert.Equal(before,f.Snapshot());
        request=Guid.NewGuid(); await f.Call("POST",CreateInput("無制限",request),201); before=f.Snapshot();
        await f.Call("POST",new{requestId=request,name="無制限",incomeExpense="Expense",taxCategoryCode="Standard",validFrom=(string?)null,validTo=(string?)null});
        Assert.Equal(before,f.Snapshot());
    }

    [Theory]
    [InlineData("Editor")] [InlineData("Viewer")]
    public async Task NewIncomeAndPeriodFieldsRemainAdminOnly(string role)
    {
        await using var f=await Fixture.Create(); var row=await f.Category(); f.Role(role); var before=f.Snapshot();
        await f.Call("PATCH",new{revision=Revision(row),incomeExpense="Income",validFrom="2026-04-01",validTo="2026-09-30"},403,Path(row)); Assert.Equal(before,f.Snapshot());
    }
}
