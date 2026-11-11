using System.Globalization;
using System.Net;
using System.Text.Json;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Theory]
    [InlineData("date", "transactionAt")] [InlineData("name", "shopName")] [InlineData("groups", "groups")]
    [InlineData("details", "groups[0].details")] [InlineData("quantity", "groups[0].details[0].quantity")]
    [InlineData("discount", "groups[0].details[0].discountRate")] [InlineData("order", "groups[0].details[0].displayOrder")]
    [InlineData("person", "groups[0].details[0].personIds[1]")] [InlineData("id", "groups[0].groupId")]
    [InlineData("period", "recurrenceUnit")] [InlineData("regular", "groups")]
    public void WriteInputRejectsInvalidValuesWithoutCorrection(string kind, string field)
    {
        var input = Snapshot(); var group = input.Groups[0]; var row = group.Details[0];
        input = kind switch {
            "date" => input with { TransactionAt = At.AddTicks(1) }, "name" => input with { ShopName = new string('あ', 101) },
            "groups" => input with { Groups = [] }, "details" => input with { Groups = [group with { Details = [] }] },
            "quantity" => input with { Groups = [group with { Details = [row with { Quantity = 0 }] }] },
            "discount" => input with { Groups = [group with { Details = [row with { DiscountRate = 100.01m }] }] },
            "order" => input with { Groups = [group with { Details = [row with { DisplayOrder = -1 }] }] },
            "person" => input with { Groups = [group with { Details = [row with { PersonIds = [Owner, Owner] }] }] },
            "id" => input with { Groups = [group with { GroupId = Guid.Empty }] },
            "period" => input with { RecurrenceInterval = 1 }, _ => input with { RecurrenceInterval = 1, RecurrenceUnit = "Month" }
        };
        var error = Assert.Throws<ReceiptInputException>(() => ReceiptWriteRequest.Create(Ledger, input)); Assert.Equal(field, error.Field);
    }
    [Theory]
    [InlineData(DateTimeKind.Unspecified)] [InlineData(DateTimeKind.Local)] [InlineData(DateTimeKind.Utc)]
    public async Task WriteUsesLocalComponentsAndInvariantDecimalStrings(DateTimeKind kind)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var input = Snapshot() with { TransactionAt = DateTime.SpecifyKind(At, kind) };
            using var f = new WriteFixture((_, _) => Task.FromResult(Written(ReceiptWriteOperation.Create))); await Write(f, WriteRequest(ReceiptWriteOperation.Create, input));
            using var body = JsonDocument.Parse(Assert.Single(f.Requests).Body);
            Assert.Equal("2026-11-10T23:59:59", body.RootElement.GetProperty("transactionAt").GetString()); Assert.Equal("12.50", body.RootElement.GetProperty("groups")[0].GetProperty("details")[0].GetProperty("discountRate").GetString());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
    [Fact]
    public async Task RequestCannotBeSentThroughAnotherLedgerOrOperation()
    {
        using var f = new WriteFixture((_, _) => throw new InvalidOperationException());
        await Assert.ThrowsAsync<ReceiptInputException>(() => f.Workspace.CreateAsync(ReceiptWriteRequest.Create(Guid.NewGuid(), Snapshot())));
        await Assert.ThrowsAsync<ReceiptInputException>(() => f.Workspace.UpdateAsync(WriteRequest(ReceiptWriteOperation.Delete))); Assert.Empty(f.Requests);
    }
    [Fact]
    public async Task ProactiveRefreshCannotChangeCapturedSnapshotAndNewAccountCannotSendIt()
    {
        var input = Snapshot(); var rows = input.Groups.ToArray(); var request = WriteRequest(ReceiptWriteOperation.Update, input with { Groups = rows });
        using var f = new WriteFixture((message, _) => {
            if (message.RequestUri!.AbsolutePath == "/connect/token") { rows[0] = rows[0] with { Memo = "更新中に変更" }; return Task.FromResult(RefreshedTokens()); }
            return Task.FromResult(Written(ReceiptWriteOperation.Update));
        });
        f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1)); await Write(f, request);
        Assert.Equal(2, f.Requests.Count); Assert.Equal("/connect/token", new Uri(f.Requests[0].Uri).AbsolutePath);
        using var json = JsonDocument.Parse(f.Requests[1].Body); Assert.Equal(request.RequestId, json.RootElement.GetProperty("requestId").GetGuid()); Assert.Equal(Owner, json.RootElement.GetProperty("revision").GetGuid()); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("groups")[0].GetProperty("memo").ValueKind);
        f.Store.Current = new("other", "other-refresh", DateTimeOffset.UtcNow.AddMinutes(5));
        await Assert.ThrowsAsync<AuthenticationContextChangedException>(() => Write(f, request)); Assert.Equal(2, f.Requests.Count);
    }
    [Fact]
    public async Task AuthenticationNetworkFailureBeforeDispatchIsNotAnUncertainReceiptWrite()
    {
        using var f = new WriteFixture((_, _) => throw new HttpRequestException()); f.Store.Current = new("old", "refresh", DateTimeOffset.UtcNow.AddSeconds(-1));
        await Assert.ThrowsAsync<ApiConnectionException>(() => Write(f, WriteRequest(ReceiptWriteOperation.Create)));
        Assert.Equal("/connect/token", new Uri(Assert.Single(f.Requests).Uri).AbsolutePath); Assert.NotNull(f.Store.Current);
    }
}
