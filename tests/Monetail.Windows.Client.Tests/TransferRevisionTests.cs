using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData("detail", "v4")] [InlineData("detail", "v7")] [InlineData("detail", "v1")] [InlineData("detail", "variant")] [InlineData("detail", "empty")]
    [InlineData("update", "v4")] [InlineData("update", "v7")] [InlineData("update", "v1")] [InlineData("update", "variant")] [InlineData("update", "empty")]
    [InlineData("delete", "v4")] [InlineData("delete", "v7")] [InlineData("delete", "v1")] [InlineData("delete", "variant")] [InlineData("delete", "empty")]
    public async Task TransferRevisionAcceptsOnlyRfcUuidV4InDetailAndConflict(string operation, string kind)
    {
        var revision = kind switch {
            "v4" => Guid.NewGuid(), "v7" => Guid.CreateVersion7(), "v1" => Guid.Parse("01234567-89ab-1def-8123-456789abcdef"),
            "variant" => Guid.Parse("01234567-89ab-4def-0123-456789abcdef"), _ => Guid.Empty
        };
        var latest = Detail() with { Revision = revision };
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(operation == "detail" ? Json(latest) : Json(TransferConflict("revision_conflict", latest), HttpStatusCode.Conflict)));
        if (operation == "detail")
        {
            if (kind == "v4") Assert.Equal(revision, (await f.Workspace.GetAsync(Transfer)).Revision);
            else await Assert.ThrowsAsync<ApiConnectionException>(() => f.Workspace.GetAsync(Transfer));
        }
        else
        {
            var request = TransferRequest(operation == "update" ? TransferWriteOperation.Update : TransferWriteOperation.Delete);
            if (kind == "v4")
            {
                var error = await Assert.ThrowsAsync<TransferRequestException>(() => TransferWrite(f, request));
                Assert.Equal("revision_conflict", error.Code); Assert.Equal(revision, error.LatestTransfer!.Revision);
            }
            else
            {
                var error = await Assert.ThrowsAsync<TransferWriteUncertainException>(() => TransferWrite(f, request));
                Assert.Equal(TransferWriteFailureKind.InvalidResponse, error.Kind);
            }
            Assert.Equal(Revision, request.Revision);
        }
        Assert.Single(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TransferRevisionV4RequirementDoesNotRestrictTransferOrPaymentMethodIds()
    {
        var latest = Detail() with { TransferId = Guid.CreateVersion7(), FromPaymentMethodId = Guid.CreateVersion7(), ToPaymentMethodId = Guid.CreateVersion7() };
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(Json(latest)));
        Assert.Equal(latest with { Memo = null }, await f.Workspace.GetAsync(latest.TransferId)); Assert.Single(f.Requests);
    }
}
