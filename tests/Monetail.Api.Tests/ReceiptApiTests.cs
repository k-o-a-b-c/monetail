using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class ReceiptWriteApiTests
{
    [Fact]
    public async Task PostAndDetailHaveExactShapeDIdentifiersAndNoMutationOnRead()
    {
        await using var f = await RecordApiFixture.Create();
        var input = f.Input(); var id = await f.CreateReceipt(input); var before = f.Snapshot();
        var detail = await f.Get(id);
        Keys(detail, "receiptId", "transactionAt", "shopName", "shopLocation", "paymentMethodId", "taxInclusionMode", "taxRoundingMode", "recurrenceInterval", "recurrenceUnit", "memo", "revision", "groups");
        Assert.Equal("2026-11-10T12:34:56", detail["transactionAt"]!.GetValue<string>());
        foreach (var name in new[] { "shopLocation", "recurrenceInterval", "recurrenceUnit", "memo" }) Assert.Null(detail[name]);
        AssertD(detail["receiptId"]!, '7'); AssertD(detail["revision"]!, '4'); AssertD(detail["paymentMethodId"]!);
        var group = Group(detail); Keys(group, "groupId", "categoryId", "memo", "details"); AssertD(group["groupId"]!, '7');
        var line = Line(detail); Keys(line, "detailId", "displayOrder", "name", "unitPrice", "quantity", "unitDiscountAmount", "discountRate", "taxRate", "expenseType", "occurrenceType", "memo", "personIds");
        AssertD(line["detailId"]!, '7'); Assert.Null(group["memo"]); Assert.Null(line["memo"]);
        Assert.Equal(new[] { D(f.Owner), D(f.Other) }, line["personIds"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(110L, f.App.Database.Scalar("SELECT total_amount FROM ledger_receipts"));
    }

    [Fact]
    public async Task PutMaintainsChildrenAndNoChangeLeavesRevisionAndAuditUntouched()
    {
        await using var f = await RecordApiFixture.Create(); var id = await f.CreateReceipt(f.Input()); var original = await f.Get(id);
        var request = f.Update(original); Line(request)["unitPrice"] = 200;
        await f.Call("PUT", request, 204, "/" + id); var changed = await f.Get(id);
        Assert.Equal(Group(original)["groupId"]!.GetValue<string>(), Group(changed)["groupId"]!.GetValue<string>());
        Assert.Equal(Line(original)["detailId"]!.GetValue<string>(), Line(changed)["detailId"]!.GetValue<string>());
        Assert.NotEqual(original["revision"]!.GetValue<string>(), changed["revision"]!.GetValue<string>());
        f.App.Database.Execute("CREATE TRIGGER no_parent_update BEFORE UPDATE ON ledger_receipts BEGIN SELECT RAISE(ABORT,'unchanged_parent_update'); END");
        var before = f.BusinessSnapshot(); var unchanged = f.Update(changed);
        Line(unchanged)["taxRate"] = "010.00"; Line(unchanged)["discountRate"] = "00.0";
        Line(unchanged)["personIds"] = new JsonArray(D(f.Other), D(f.Owner));
        await f.Call("PUT", unchanged, 204, "/" + id);
        Assert.Equal(before, f.BusinessSnapshot()); Assert.Equal(3L, f.App.Database.Scalar("SELECT COUNT(*) FROM operation_requests WHERE operation_type LIKE 'receipt.%'"));
    }

    [Fact]
    public async Task BlankMemosAreNullInStorageReadsAndPostReplay()
    {
        await using var f = await RecordApiFixture.Create();
        var input = f.Input(); input["memo"] = ""; Group(input)["memo"] = ""; Line(input)["memo"] = "";
        var id = await f.CreateReceipt(input);
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipts WHERE memo IS NULL"));
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipt_groups WHERE memo IS NULL"));
        Assert.Equal(1L, f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_receipt_details WHERE memo IS NULL"));
        var detail = await f.Get(id);
        Assert.Null(detail["memo"]); Assert.Null(Group(detail)["memo"]); Assert.Null(Line(detail)["memo"]);
        var listing = (await f.Call("GET", null, 200, "?month=2026-11"))!;
        Assert.Null(listing["receipts"]![0]!["memo"]);
        var before = f.Snapshot();
        input["memo"] = null; Group(input)["memo"] = null; Line(input)["memo"] = null;
        await f.Call("POST", input, 201);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task LegacyBlankMemosReadAsNullAndDoNotCauseAnUpdate()
    {
        await using var f = await RecordApiFixture.Create();
        var id = await f.CreateReceipt(f.Input());
        // 新しいCHECKを迂回して、旧DBに残り得る空文字のデータを再現する。
        f.App.Database.Execute("PRAGMA ignore_check_constraints=ON");
        try
        {
            f.App.Database.Execute("UPDATE ledger_receipts SET memo=''");
            f.App.Database.Execute("UPDATE ledger_receipt_groups SET memo=''");
            f.App.Database.Execute("UPDATE ledger_receipt_details SET memo=''");
        }
        finally { f.App.Database.Execute("PRAGMA ignore_check_constraints=OFF"); }
        var detail = await f.Get(id);
        Assert.Null(detail["memo"]); Assert.Null(Group(detail)["memo"]); Assert.Null(Line(detail)["memo"]);
        var listing = (await f.Call("GET", null, 200, "?month=2026-11"))!;
        Assert.Null(listing["receipts"]![0]!["memo"]);
        f.App.Database.Execute("CREATE TRIGGER no_parent_update BEFORE UPDATE ON ledger_receipts BEGIN SELECT RAISE(ABORT,'unchanged_parent_update'); END");
        var before = f.BusinessSnapshot();
        var update = f.Update(detail); update["memo"] = ""; Group(update)["memo"] = ""; Line(update)["memo"] = "";
        await f.Call("PUT", update, 204, "/" + id);
        Assert.Equal(before, f.BusinessSnapshot());
    }

    [Theory]
    [InlineData(100, false, true)]
    [InlineData(101, false, false)]
    [InlineData(50, true, true)]
    [InlineData(51, true, false)]
    public async Task ParentMemoLimitCountsUtf16CodeUnits(int count, bool emoji, bool valid)
    {
        await using var f = await RecordApiFixture.Create();
        var memo = emoji ? string.Concat(Enumerable.Repeat("😀", count)) : new string('a', count);
        var input = f.Input(); input["memo"] = memo;
        if (valid)
        {
            var id = await f.CreateReceipt(input);
            Assert.Equal(memo, (await f.Get(id))["memo"]!.GetValue<string>());
        }
        else
        {
            var before = f.Snapshot();
            var error = (await f.Call("POST", input, 400))!;
            Assert.Equal("invalid_input", error["code"]!.GetValue<string>());
            Assert.Equal("memo", error["field"]!.GetValue<string>());
            Assert.Equal(before, f.Snapshot());
        }
    }

    [Fact]
    public async Task PutRejectsOversizeParentMemoWithoutMutatingExistingReceipt()
    {
        await using var f = await RecordApiFixture.Create();
        var id = await f.CreateReceipt(f.Input());
        var before = f.Snapshot();
        var update = f.Update(await f.Get(id)); update["memo"] = new string('x', 101);
        var error = (await f.Call("PUT", update, 400, "/" + id))!;
        Assert.Equal("invalid_input", error["code"]!.GetValue<string>());
        Assert.Equal("memo", error["field"]!.GetValue<string>());
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("ledger_receipts")]
    [InlineData("ledger_receipt_groups")]
    [InlineData("ledger_receipt_details")]
    public async Task MemoDatabaseConstraintsRejectEmptyText(string table)
    {
        await using var f = await RecordApiFixture.Create();
        await f.CreateReceipt(f.Input());
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => f.App.Database.Execute($"UPDATE {table} SET memo=''"));
    }

    [Fact]
    public async Task DeleteReplayUsesSuccessRecordWithoutRequiringDeletedResource()
    {
        await using var f = await RecordApiFixture.Create(); var id = await f.CreateReceipt(f.Input()); var detail = await f.Get(id);
        var input = new JsonObject { ["requestId"] = Guid.NewGuid().ToString("D"), ["revision"] = detail["revision"]!.GetValue<string>() };
        await f.Call("DELETE", input, 204, "/" + id); var before = f.Snapshot();
        await f.Call("DELETE", input, 204, "/" + id); Assert.Equal(before, f.Snapshot());
        foreach (var table in new[] { "ledger_receipts", "ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people" })
            Assert.Equal(0L, f.App.Database.Scalar("SELECT COUNT(*) FROM " + table));
        await f.Call("GET", null, 404, "/" + id);
    }

    private static JsonObject Group(JsonObject root, int index = 0) => root["groups"]![index]!.AsObject();
    private static JsonObject Line(JsonObject root, int group = 0, int index = 0) => Group(root, group)["details"]![index]!.AsObject();
}
