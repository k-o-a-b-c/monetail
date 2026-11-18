using System.Net;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class ReceiptsWorkspaceTests
{
    [Fact]
    public async Task ReceiptFocusKeepsAllFiltersAndUsesOrdinaryReturnedCursor()
    {
        var query = new ReceiptListQuery("2026-11", Owner, Payment, " ＡＢＣ+& ", focusReceiptId: Receipt);
        using var f = new Fixture(request =>
        {
            var values = Query(request);
            Assert.Equal(5, values.Count);
            Assert.Equal(query.Month, values["month"]);
            Assert.Equal(query.Q, values["q"]);
            Assert.Equal(Owner.ToString("D"), values["ownerPersonId"]);
            Assert.Equal(Payment.ToString("D"), values["paymentMethodId"]);
            if (values.TryGetValue("focusReceiptId", out var focus))
            {
                Assert.Equal(Receipt.ToString("D"), focus);
                Assert.False(values.ContainsKey("cursor"));
            }
            else
            {
                Assert.Equal("opaque/+next", values["cursor"]);
                Assert.False(values.ContainsKey("focusReceiptId"));
            }
            return Json(Page(cursor: "opaque/+next"));
        });
        var page = await f.Workspace.ListAsync(query);
        Assert.Equal(Receipt, Assert.Single(page.Receipts).ReceiptId);
        Assert.Equal("opaque/+next", page.NextCursor);
        Assert.Single(f.Requests);
        await f.Workspace.ListAsync(new(query.Month, query.OwnerPersonId, query.PaymentMethodId, query.Q, page.NextCursor));
        Assert.Equal(2, f.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opaque")]
    public void ReceiptFocusRejectsEmptyIdAndAnyCursorCombination(string? cursor)
    {
        var invalid = Assert.Throws<ReceiptInputException>(() => new ReceiptListQuery("all", focusReceiptId: Guid.Empty));
        Assert.Equal("focusReceiptId", invalid.Field);
        if (cursor is not null)
            Assert.Equal("focusReceiptId", Assert.Throws<ReceiptInputException>(() => new ReceiptListQuery("all", cursor: cursor, focusReceiptId: Receipt)).Field);
        else
            Assert.Equal(Receipt, new ReceiptListQuery("all", focusReceiptId: Receipt).FocusReceiptId);
    }

    [Fact]
    public async Task ReceiptFocusSurvivesAuthenticationRefreshWithoutChangingRequest()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token" ? RefreshedTokens()
            : request.Headers.Authorization?.Parameter == "old" ? new(HttpStatusCode.Unauthorized) : Json(Page()));
        await f.Workspace.ListAsync(new("all", q: " 店舗 ", focusReceiptId: Receipt));
        Assert.Equal(3, f.Requests.Count);
        Assert.Equal(f.Requests[0].Uri, f.Requests[2].Uri);
        Assert.Contains("focusReceiptId=" + Receipt.ToString("D"), f.Requests[0].Uri);
        Assert.Equal("updated", f.Requests[2].Bearer);
    }

    [Theory]
    [InlineData(404, "not_found")]
    [InlineData(400, "invalid_input")]
    public async Task ReceiptFocusBusinessFailureDoesNotAutomaticallySearchFromTheStart(int status, string code)
    {
        using var f = new Fixture(_ => Json(new { code, field = "focusReceiptId" }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ReceiptRequestException>(() => f.Workspace.ListAsync(new("all", focusReceiptId: Receipt)));
        Assert.Equal(code, error.Code);
        Assert.Equal("focusReceiptId", error.Field);
        Assert.Single(f.Requests);
    }
    [Theory]
    [InlineData("empty")]
    [InlineData("different")]
    [InlineData("second")]
    public async Task ReceiptFocusRejectsSuccessfulPagesWhoseFirstRowIsNotTheTarget(string kind)
    {
        var other = Summary(Guid.NewGuid());
        var rows = kind switch
        {
            "empty" => Array.Empty<ReceiptSummaryInfo>(),
            "different" => [other],
            _ => [other, Summary()]
        };
        using var f = new Fixture(_ => Json(Page(rows)));
        await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all", focusReceiptId: Receipt)));
        Assert.Single(f.Requests);
    }
}
