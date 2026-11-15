using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Theory]
    [InlineData("fraction", "transactionAt")] [InlineData("null-name", "name")] [InlineData("empty-name", "name")]
    [InlineData("unicode-space", "name")] [InlineData("long-name", "name")] [InlineData("surrogate-name", "name")]
    [InlineData("from", "fromPaymentMethodId")] [InlineData("to", "toPaymentMethodId")] [InlineData("same", "toPaymentMethodId")]
    [InlineData("zero", "amount")] [InlineData("negative", "amount")] [InlineData("long-memo", "memo")] [InlineData("surrogate-memo", "memo")]
    [InlineData("cr", "memo")] [InlineData("lf", "memo")] [InlineData("vt", "memo")] [InlineData("ff", "memo")]
    [InlineData("nel", "memo")] [InlineData("ls", "memo")] [InlineData("ps", "memo")]
    public void TransferWriteInputRejectsInvalidValuesWithoutCorrectingSource(string invalid, string field)
    {
        var original = TransferSnapshot();
        var input = invalid switch {
            "fraction" => original with { TransactionAt = At.AddTicks(1) }, "null-name" => original with { Name = null! },
            "empty-name" => original with { Name = "" }, "unicode-space" => original with { Name = " \t\u3000\u00a0" },
            "long-name" => original with { Name = new string('a', 101) }, "surrogate-name" => original with { Name = string.Concat(Enumerable.Repeat("😀", 50)) + "a" },
            "from" => original with { FromPaymentMethodId = Guid.Empty }, "to" => original with { ToPaymentMethodId = Guid.Empty },
            "same" => original with { ToPaymentMethodId = From }, "zero" => original with { Amount = 0 }, "negative" => original with { Amount = int.MinValue },
            "long-memo" => original with { Memo = new string('m', 101) }, "surrogate-memo" => original with { Memo = string.Concat(Enumerable.Repeat("😀", 50)) + "a" },
            _ => original with { Memo = "a" + (invalid switch { "cr" => "\r", "lf" => "\n", "vt" => "\v", "ff" => "\f", "nel" => "\u0085", "ls" => "\u2028", _ => "\u2029" }) + "b" }
        };
        foreach (var operation in new[] { TransferWriteOperation.Create, TransferWriteOperation.Update })
        {
            var before = input with { }; var error = Assert.Throws<TransferInputException>(() => TransferRequest(operation, input));
            Assert.Equal(field, error.Field); Assert.Equal(before, input);
        }
        Assert.Equal(TransferSnapshot(), original);
    }

    [Theory]
    [InlineData(TransferWriteOperation.Create)] [InlineData(TransferWriteOperation.Update)] [InlineData(TransferWriteOperation.Delete)]
    public async Task TransferWriteRejectsDifferentLedgerOrOperationBeforeAnyHttp(TransferWriteOperation operation)
    {
        var request = TransferRequest(operation); using var f = new TransferWriteFixture((_, _) => throw new InvalidOperationException());
        var other = new TransfersWorkspace(f.Api, f.Session, Guid.NewGuid());
        foreach (var target in new[] { f.Workspace, other })
        {
            foreach (var called in Enum.GetValues<TransferWriteOperation>())
            {
                if (target == f.Workspace && called == operation) continue;
                var error = await Assert.ThrowsAsync<TransferInputException>(() => called switch {
                    TransferWriteOperation.Create => target.CreateAsync(request), TransferWriteOperation.Update => target.UpdateAsync(request), _ => target.DeleteAsync(request)
                }); Assert.Equal("requestId", error.Field);
            }
        }
        // Transportを直接使う呼出しでも、異なる操作への要求流用を防ぐ。
        if (operation != TransferWriteOperation.Create) await Assert.ThrowsAsync<TransferInputException>(() => f.Api.CreateTransferAsync("old", request));
        if (operation != TransferWriteOperation.Update) await Assert.ThrowsAsync<TransferInputException>(() => f.Api.UpdateTransferAsync("old", request));
        if (operation != TransferWriteOperation.Delete) await Assert.ThrowsAsync<TransferInputException>(() => f.Api.DeleteTransferAsync("old", request));
        Assert.Empty(f.Requests);
    }

    [Theory]
    [InlineData("ledger")] [InlineData("transfer")] [InlineData("revision-zero")] [InlineData("revision-v7")] [InlineData("revision-variant")]
    public void TransferWriteIdentifiersAndRevisionV4AreValidated(string invalid)
    {
        var ledger = invalid == "ledger" ? Guid.Empty : Ledger; var id = invalid == "transfer" ? Guid.Empty : Transfer;
        var version = invalid switch { "revision-zero" => Guid.Empty, "revision-v7" => Guid.CreateVersion7(), "revision-variant" => Guid.Parse("01234567-89ab-4def-0123-456789abcdef"), _ => Revision };
        if (invalid == "ledger") Assert.Equal("ledgerId", Assert.Throws<TransferInputException>(() => TransferWriteRequest.Create(ledger, TransferSnapshot())).Field);
        foreach (var operation in new[] { TransferWriteOperation.Update, TransferWriteOperation.Delete })
        {
            var error = Assert.Throws<TransferInputException>(() => operation == TransferWriteOperation.Update ? TransferWriteRequest.Update(ledger, id, version, TransferSnapshot()) : TransferWriteRequest.Delete(ledger, id, version));
            Assert.Equal(invalid == "ledger" ? "ledgerId" : invalid == "transfer" ? "transferId" : "revision", error.Field);
        }
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)] [InlineData(DateTimeKind.Local)] [InlineData(DateTimeKind.Utc)]
    public async Task TransferWriteSerializesWallClockSecondsWithoutTimezoneConversion(DateTimeKind kind)
    {
        foreach (var at in new[] { DateTime.SpecifyKind(DateTime.MinValue, kind), DateTime.SpecifyKind(At, kind), new DateTime(9999, 12, 31, 23, 59, 59, kind) })
        {
            using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
            await f.Workspace.CreateAsync(TransferWriteRequest.Create(Ledger, TransferSnapshot() with { TransactionAt = at }));
            using var doc = JsonDocument.Parse(Assert.Single(f.Requests).Body);
            Assert.Equal(at.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), doc.RootElement.GetProperty("transactionAt").GetString());
        }
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t\u3000")]
    public async Task TransferWriteNormalizesOnlyEmptyMemoAndNeverTrimsName(string? memo)
    {
        var snapshot = TransferSnapshot() with { Name = " Ａ銀行　 ", Memo = memo, Amount = 1 };
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
        await f.Workspace.CreateAsync(TransferWriteRequest.Create(Ledger, snapshot)); using var doc = JsonDocument.Parse(Assert.Single(f.Requests).Body);
        Assert.Equal(snapshot.Name, doc.RootElement.GetProperty("name").GetString()); Assert.Equal(memo is "" ? null : memo, doc.RootElement.GetProperty("memo").GetString()); Assert.Equal(memo, snapshot.Memo);
    }

    [Fact]
    public async Task TransferWriteAcceptsUtf16BoundariesAndDoesNotInventClientPermissionRules()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 50)); var input = TransferSnapshot() with { Name = text, Memo = text, FromPaymentMethodId = Guid.CreateVersion7() };
        using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
        await f.Workspace.CreateAsync(TransferWriteRequest.Create(Ledger, input)); using var doc = JsonDocument.Parse(Assert.Single(f.Requests).Body);
        Assert.Equal(text, doc.RootElement.GetProperty("name").GetString()); Assert.Equal(text, doc.RootElement.GetProperty("memo").GetString());
        Assert.Equal(input.FromPaymentMethodId, doc.RootElement.GetProperty("fromPaymentMethodId").GetGuid());
        Assert.Throws<ArgumentNullException>(() => TransferWriteRequest.Create(Ledger, null!)); Assert.Throws<ArgumentNullException>(() => TransferWriteRequest.Update(Ledger, Transfer, Revision, null!));
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task TransferWriteNameAndMemoAcceptInclusiveLengthLimits(int length)
    {
        var text = new string('名', length); using var f = new TransferWriteFixture((_, _) => Task.FromResult(TransferWritten(TransferWriteOperation.Create)));
        await f.Workspace.CreateAsync(TransferWriteRequest.Create(Ledger, TransferSnapshot() with { Name = text, Memo = text }));
        using var doc = JsonDocument.Parse(Assert.Single(f.Requests).Body); Assert.Equal(text, doc.RootElement.GetProperty("name").GetString()); Assert.Equal(text, doc.RootElement.GetProperty("memo").GetString());
    }
}
