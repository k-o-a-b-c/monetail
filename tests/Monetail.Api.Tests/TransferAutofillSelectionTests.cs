using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferAutofillApiTests
{
    [Theory]
    [InlineData("atm")] [InlineData("ＡＴＭ")] [InlineData("ATM支店")] [InlineData(" ATM")] [InlineData("ATM ")]
    [InlineData("えーてぃーえむ")] [InlineData("①ATM")]
    public async Task TransferAutofillMatchesOnlyOriginalNameWithoutNormalizationOrSubstring(string name)
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "ATM"); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(name)), null, null);
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery("ATM")), f.Payment, f.OtherPayment); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillPrioritizesUpdateUtcThenBinaryIdInsteadOfTransactionOrCreationDate()
    {
        await using var f = await RecordApiFixture.Create(); AutofillTransfer(f, 1, f.Payment, f.OtherPayment, updated: "2026-11-11 00:00:00");
        AutofillTransfer(f, 2, f.OtherPayment, f.Payment, updated: "2026-11-12 00:00:00"); AssertTransferPair(await TransferAutofill(f), f.OtherPayment, f.Payment);
        var latest = AutofillTransfer(f, 3, f.Payment, f.OtherPayment, updated: "2026-11-12 00:00:00");
        f.App.Database.Execute($"UPDATE ledger_transfers SET transaction_at='9999-12-31T23:59:59',created_at_utc='2026-11-13 00:00:00' WHERE transfer_id<>'{latest}'; UPDATE ledger_transfers SET transaction_at='1900-01-01T00:00:00' WHERE transfer_id='{latest}'");
        var before = f.Snapshot(); AssertTransferPair(await TransferAutofill(f), f.Payment, f.OtherPayment); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillDoesNotPrioritizeUsageCount()
    {
        await using var f = await RecordApiFixture.Create(); SeedTransferList(f, 201); AutofillTransfer(f, 1000, f.OtherPayment, f.Payment, updated: "2026-11-12 00:00:00"); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f), f.OtherPayment, f.Payment); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferAutofillSkipsUnusableHistoriesWithoutCombiningTheirSeparateUsableSides()
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f);
        AutofillTransfer(f, 1, f.Payment, own); AutofillTransfer(f, 2, f.OtherPayment, f.Payment, updated: "2026-11-11 00:00:00"); AutofillTransfer(f, 3, own, f.OtherPayment, updated: "2026-11-12 00:00:00");
        f.App.Database.Execute($"UPDATE ledger_payment_methods SET valid_to='2026-11-09' WHERE payment_method_id='{f.OtherPayment}'"); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f), f.Payment, own); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("from", "payment", false)] [InlineData("to", "payment", false)] [InlineData("from", "person", false)] [InlineData("to", "person", false)]
    [InlineData("from", "payment", true)] [InlineData("to", "payment", true)] [InlineData("from", "person", true)] [InlineData("to", "person", true)]
    public async Task TransferAutofillChecksBothPaymentAndPersonPeriodsWithInclusiveBoundaries(string side, string kind, bool specified)
    {
        await using var f = await RecordApiFixture.Create(); TransferCandidate(f, 1, "振替"); var id = kind == "payment" ? (side == "from" ? f.Payment : f.OtherPayment) : (side == "from" ? f.Owner : f.Other);
        var table = kind == "payment" ? "ledger_payment_methods" : "ledger_people"; var column = kind == "payment" ? "payment_method_id" : "person_id";
        f.App.Database.Execute($"UPDATE {table} SET valid_from='2026-11-10',valid_to='2026-11-10' WHERE {column}='{id}'"); var before = f.Snapshot(); var from = specified ? D(f.Payment) : null;
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(from: from)), f.Payment, f.OtherPayment);
        foreach (var day in new[] { "2026-11-09", "2026-11-11" }) AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(date: day, from: from)), null, null);
        Assert.Equal(before, f.Snapshot()); f.App.Database.Execute($"UPDATE {table} SET valid_from=NULL,valid_to=NULL WHERE {column}='{id}'"); before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(date: "1900-01-01", from: from)), f.Payment, f.OtherPayment); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TransferAutofillSpecifiedSourceSelectsOnlyMatchingHistoryAndDoesNotEchoSourceWithoutMatch(bool history)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); AutofillTransfer(f, 1, f.OtherPayment, f.Payment, updated: "2026-11-12 00:00:00");
        if (history) AutofillTransfer(f, 2, f.Payment, own, updated: "2026-11-01 00:00:00"); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(from: D(f.Payment).ToUpperInvariant())), history ? f.Payment : null, history ? own : null); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("own", false)] [InlineData("from-other", false)] [InlineData("to-other", false)] [InlineData("own", true)]
    public async Task TransferAutofillEditorRequiresBothOwnersAndSkipsOtherPeopleHistory(string mode, bool specified)
    {
        await using var f = await RecordApiFixture.Create(); var own = await OwnTransferPayment(f); var from = mode == "from-other" ? f.OtherPayment : f.Payment; var to = mode == "to-other" ? f.OtherPayment : own;
        AutofillTransfer(f, 1, from, to); f.Role("Editor", f.Owner); var before = f.Snapshot();
        AssertTransferPair(await TransferAutofill(f, TransferAutofillQuery(from: specified ? D(f.Payment) : null)), mode == "own" ? f.Payment : null, mode == "own" ? own : null); Assert.Equal(before, f.Snapshot());
    }

    private static string AutofillTransfer(RecordApiFixture f, int number, string from, string to, string name = "振替", string updated = "2026-11-10 12:00:00")
    {
        var id = SeedTransferList(f, 1, from, to, name: name, start: number - 1)[0]; using var connection = f.App.Database.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ledger_transfers SET updated_at_utc=@updated WHERE ledger_id=@ledger AND transfer_id=@id"; command.Parameters.AddWithValue("@updated", updated); command.Parameters.AddWithValue("@ledger", f.Ledger); command.Parameters.AddWithValue("@id", id); command.ExecuteNonQuery(); return id;
    }
}
