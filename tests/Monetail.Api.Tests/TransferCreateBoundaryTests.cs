using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Monetail.Api.Tests.RecordApiTestData;

namespace Monetail.Api.Tests;

public sealed partial class TransferCreateApiTests
{
    [Theory]
    [InlineData("fromPaymentMethodId")] [InlineData("toPaymentMethodId")]
    public async Task TransferCreateRejectsOtherLedgerPaymentsWithoutLeakingExistence(string field)
    {
        await using var f = await RecordApiFixture.Create(); using var created = await f.Client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "別家計簿" });
        var ledger = (await created.Content.ReadFromJsonAsync<JsonObject>())!["ledgerId"]!.GetValue<string>();
        using var person = await f.Client.PostAsJsonAsync($"/ledgers/{D(ledger)}/people", new { requestId = Guid.NewGuid(), name = "別人物" });
        var owner = (await person.Content.ReadFromJsonAsync<JsonObject>())!["people"]![0]!["personId"]!.GetValue<string>();
        using var payment = await f.Client.PostAsJsonAsync($"/ledgers/{D(ledger)}/payment-methods", new { requestId = Guid.NewGuid(), ownerPersonId = D(owner), name = "別口座" });
        var foreign = (await payment.Content.ReadFromJsonAsync<JsonObject>())!["paymentMethodId"]!.GetValue<string>();
        var input = TransferInput(f); input[field] = D(foreign); var before = f.Snapshot(); var denied = await TransferCreateError(f, input, 404);
        input[field] = Guid.NewGuid().ToString("D"); var absent = await TransferCreateError(f, input, 404);
        Assert.Equal("not_found", denied["code"]!.GetValue<string>()); Assert.Equal(denied.ToJsonString(), absent.ToJsonString()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("requestId", false)] [InlineData("requestId", true)]
    [InlineData("fromPaymentMethodId", false)] [InlineData("fromPaymentMethodId", true)]
    [InlineData("toPaymentMethodId", false)] [InlineData("toPaymentMethodId", true)]
    public async Task TransferCreateRequiresStrictDUuidForm(string field, bool whitespace)
    {
        await using var f = await RecordApiFixture.Create(); var input = TransferInput(f); var value = input[field]!.GetValue<string>(); input[field] = whitespace ? " " + value + " " : Guid.Parse(value).ToString("N");
        var before = f.Snapshot(); Assert.Equal(field, (await TransferCreateError(f, input, 400))["field"]!.GetValue<string>()); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("anonymous", 401)] [InlineData("removed", 404)] [InlineData("expired", 401)]
    public async Task TransferCreateRequiresCurrentAuthenticationAndLedgerParticipation(string kind, int status)
    {
        await using var f = await RecordApiFixture.Create(); using var anonymous = f.App.Client();
        if (kind == "removed") f.App.Database.Execute("DELETE FROM ledger_members"); if (kind == "expired") f.App.Time.Advance(TimeSpan.FromDays(15));
        var before = f.Snapshot(); using var response = await (kind == "anonymous" ? anonymous : f.Client).PostAsJsonAsync(TransferRoot(f), TransferInput(f));
        Assert.Equal(status, (int)response.StatusCode); Assert.Null(response.Headers.Location); Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData("csrf", 400)] [InlineData("origin", 403)]
    public async Task TransferCreateKeepsExistingCsrfAndOriginBoundary(string kind, int status)
    {
        await using var f = await RecordApiFixture.Create(); if (kind == "csrf") f.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        else { f.Client.DefaultRequestHeaders.Remove("Origin"); f.Client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        var before = f.Snapshot(); using var response = await f.Client.PostAsJsonAsync(TransferRoot(f), TransferInput(f)); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public async Task TransferCreateUsesSharedTwentyWriteLimit()
    {
        await using var f = await RecordApiFixture.Create();
        for (var i = 0; i < 13; i++) await CreateTransfer(f, TransferInput(f)); // fixtureの7操作と合わせ20回。
        var before = f.Snapshot(); using var limited = await f.Client.PostAsJsonAsync(TransferRoot(f), TransferInput(f)); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var shared = await f.Client.PostAsJsonAsync($"/ledgers/{D(f.Ledger)}/people", new { requestId = Guid.NewGuid(), name = "回数制限" }); Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode); Assert.Equal(before, f.Snapshot());
    }
}
