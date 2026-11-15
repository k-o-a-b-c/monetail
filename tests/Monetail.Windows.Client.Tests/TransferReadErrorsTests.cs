using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData(400, "invalid_cursor")] [InlineData(400, "invalid_input")] [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")] [InlineData(429, "rate_limited")] [InlineData(503, "store_unavailable")]
    public async Task TransferBusinessErrorsRemainTypedAndNeverRequeryClearTokensOrExposeMessage(int status, string code)
    {
        using var f = new Fixture(_ => Json(new { code, message = "非公開のSQLや接続情報", field = "cursor", latest = (object?)null, permissions = new { currentRole = "Viewer", myPersonId = Owner } }, (HttpStatusCode)status));
        var query = new TransferListQuery("all", cursor: "unchanged"); var error = await Assert.ThrowsAsync<TransferRequestException>(() => f.Workspace.ListAsync(query));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(code, error.Code); Assert.Equal("cursor", error.Field); Assert.DoesNotContain("SQL", error.Message); Assert.Equal("unchanged", query.Cursor);
        if (status is 403 or 404) Assert.Equal(new("Viewer", Owner), error.Permissions); else Assert.Null(error.Permissions);
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)]
    public async Task TransferPermissionsMayExplicitlyIndicateNoMembership(int status)
    {
        using var f = new Fixture(_ => Json(new { code = "not_found", field = (string?)null, message = "非公開", latest = (object?)null, permissions = new { currentRole = (string?)null, myPersonId = (Guid?)null } }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<TransferRequestException>(() => f.Workspace.GetAsync(Transfer)); Assert.Equal(new(null, null), error.Permissions); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("role")] [InlineData("idN")] [InlineData("missingRole")] [InlineData("roleNullId")] [InlineData("fieldType")]
    public async Task TransferMalformedErrorMetadataIsRejected(string kind)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(new { code = "forbidden", message = "internal", field = (string?)null, latest = (object?)null, permissions = new { currentRole = "Viewer", myPersonId = Owner } }, Web)!;
        switch (kind) {
            case "role": payload["permissions"]!["currentRole"] = "Owner"; break;
            case "idN": payload["permissions"]!["myPersonId"] = Owner.ToString("N"); break;
            case "missingRole": payload["permissions"]!.AsObject().Remove("currentRole"); break;
            case "roleNullId": payload["permissions"]!["currentRole"] = null; break;
            default: payload["field"] = 1; break;
        }
        using var f = new Fixture(_ => Json(payload, HttpStatusCode.Forbidden)); await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.ListAsync(new("all"))); Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(503)]
    public async Task TransferEmptyErrorBodyStillRetainsKnownHttpStatus(int status)
    {
        using var f = new Fixture(_ => new((HttpStatusCode)status)); var error = await Assert.ThrowsAsync<TransferRequestException>(() => f.Workspace.ListAsync(new("all"))); Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TransferFocusNotFoundAndAutofillForbiddenDoNotFallbackOrEchoSpecifiedSource()
    {
        using var missing = new Fixture(_ => Json(new { code = "not_found" }, HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<TransferRequestException>(() => missing.Workspace.ListAsync(new("all", focusTransferId: Transfer))); Assert.Equal(HttpStatusCode.NotFound, error.StatusCode); Assert.Single(missing.Requests);
        using var forbidden = new Fixture(_ => Json(new { code = "forbidden", permissions = new { currentRole = "Viewer", myPersonId = (Guid?)null } }, HttpStatusCode.Forbidden));
        var denied = await Assert.ThrowsAsync<TransferRequestException>(() => forbidden.Workspace.AutofillAsync(new("振替", new(2026, 11, 10), From))); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Single(forbidden.Requests);
    }
}
