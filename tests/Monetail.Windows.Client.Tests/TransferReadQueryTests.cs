using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed partial class TransfersWorkspaceTests
{
    [Fact]
    public async Task TransferFocusPreservesOtherFiltersAndDoesNotAutomaticallyFetchNextPage()
    {
        using var f = new Fixture(request => {
            var query = Query(request); Assert.Equal(5, query.Count); Assert.Equal("2026-11", query["month"]); Assert.Equal(Owner.ToString("D"), query["ownerPersonId"]);
            Assert.Equal(From.ToString("D"), query["paymentMethodId"]); Assert.Equal("", query["q"]); Assert.Equal(Transfer.ToString("D"), query["focusTransferId"]); Assert.False(query.ContainsKey("cursor"));
            return Json(Page(cursor: "opaque-next"));
        });
        var result = await f.Workspace.ListAsync(new("2026-11", Owner, From, "", focusTransferId: Transfer)); Assert.Equal("opaque-next", result.NextCursor); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task TransferListRetainsOmittedAndEmptySearchAndCursorIndependently()
    {
        var count = 1; using var f = new Fixture(request => { var query = Query(request); Assert.Equal(count++, query.Count); return Json(Page([])); });
        await f.Workspace.ListAsync(new("all")); await f.Workspace.ListAsync(new("all", q: "")); await f.Workspace.ListAsync(new("all", q: "", cursor: "")); Assert.Equal(3, f.Requests.Count);
    }

    [Fact]
    public async Task TransferListDoesNotApplySuggestionLengthCapOrNormalizeSearch()
    {
        var text = " ＡＢＣ&+/" + new string('あ', 1001);
        using var f = new Fixture(request => { Assert.Equal(text, Query(request)["q"]); return Json(Page([])); });
        await f.Workspace.ListAsync(new("0001-01", q: text)); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task TransferSuggestionsPreserveOriginalNamesOrderHasMoreAndQueryEncoding()
    {
        var values = Enumerable.Range(0, 100).Select(i => i.ToString("000") + new string('名', 97)).Reverse().ToArray(); var search = " ＡＴＭ&+/ ";
        using var f = new Fixture(request => { Assert.EndsWith("/transfer-suggestions", request.RequestUri!.AbsolutePath); Assert.Equal(search, Query(request)["q"]); return Json(new { suggestions = values, hasMore = true }); });
        var result = await f.Workspace.SuggestAsync(new(search)); Assert.Equal(values, result.Suggestions); Assert.True(result.HasMore); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task TransferSuggestionsKeepOmittedAndEmptyQueryDifferent()
    {
        var count = 1; using var f = new Fixture(request => { var query = Query(request); if (count++ == 1) Assert.Empty(query); else Assert.Equal("", query["q"]); return Json(new { suggestions = new[] { "ABC", "abc", "ＡＢＣ" }, hasMore = false }); });
        Assert.Equal(new[] { "ABC", "abc", "ＡＢＣ" }, (await f.Workspace.SuggestAsync(new())).Suggestions); Assert.False((await f.Workspace.SuggestAsync(new(""))).HasMore); Assert.Equal(2, f.Requests.Count);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task TransferAutofillKeepsNameAndDateAndSpecifiedSourceWithoutApplyingAnything(bool specified, bool found)
    {
        var query = new TransferAutofillQuery(" ＡＴＭ&+/ ", new(2024, 2, 29), specified ? From : null);
        using var f = new Fixture(request => {
            Assert.EndsWith("/transfer-autofill", request.RequestUri!.AbsolutePath); var values = Query(request); Assert.Equal(specified ? 3 : 2, values.Count);
            Assert.Equal(query.Name, values["name"]); Assert.Equal("2024-02-29", values["transactionDate"]); if (specified) Assert.Equal(From.ToString("D"), values["fromPaymentMethodId"]);
            return Json(new { fromPaymentMethodId = found ? (Guid?)From : null, toPaymentMethodId = found ? (Guid?)To : null });
        });
        Assert.Equal(new(found ? From : null, found ? To : null), await f.Workspace.AutofillAsync(query)); Assert.Equal(" ＡＴＭ&+/ ", query.Name); Assert.Equal(specified ? (Guid?)From : null, query.FromPaymentMethodId); Assert.Single(f.Requests);
    }

    [Theory]
    [InlineData("month")] [InlineData("monthRange")] [InlineData("monthCase")] [InlineData("monthMissing")]
    [InlineData("owner")] [InlineData("payment")] [InlineData("focus")] [InlineData("focusCursor")] [InlineData("focusEmptyCursor")]
    [InlineData("ledger")] [InlineData("transfer")] [InlineData("suggestionLength")] [InlineData("nameMissing")] [InlineData("nameEmpty")] [InlineData("nameLength")] [InlineData("from")]
    public async Task TransferInvalidInputsAreTypedAndNeverSendHttp(string kind)
    {
        using var f = new Fixture(_ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<TransferInputException>(async () => {
            switch (kind) {
                case "month": await f.Workspace.ListAsync(new("2026-1")); break;
                case "monthRange": await f.Workspace.ListAsync(new("2026-13")); break;
                case "monthCase": await f.Workspace.ListAsync(new("ALL")); break;
                case "monthMissing": await f.Workspace.ListAsync(new(null!)); break;
                case "owner": await f.Workspace.ListAsync(new("all", ownerPersonId: Guid.Empty)); break;
                case "payment": await f.Workspace.ListAsync(new("all", paymentMethodId: Guid.Empty)); break;
                case "focus": await f.Workspace.ListAsync(new("all", focusTransferId: Guid.Empty)); break;
                case "focusCursor": await f.Workspace.ListAsync(new("all", cursor: "opaque", focusTransferId: Transfer)); break;
                case "focusEmptyCursor": await f.Workspace.ListAsync(new("all", cursor: "", focusTransferId: Transfer)); break;
                case "ledger": await f.Api.ListTransfersAsync("old", Guid.Empty, new("all")); break;
                case "transfer": await f.Workspace.GetAsync(Guid.Empty); break;
                case "suggestionLength": await f.Workspace.SuggestAsync(new(new string('あ', 101))); break;
                case "nameMissing": await f.Workspace.AutofillAsync(new(null!, new(2026, 11, 10))); break;
                case "nameEmpty": await f.Workspace.AutofillAsync(new("", new(2026, 11, 10))); break;
                case "nameLength": await f.Workspace.AutofillAsync(new(new string('あ', 101), new(2026, 11, 10))); break;
                default: await f.Workspace.AutofillAsync(new("振替", new(2026, 11, 10), Guid.Empty)); break;
            }
        }); Assert.Empty(f.Requests); Assert.NotNull(f.Store.Current);
    }

    [Fact]
    public async Task TransferQueryUtf16BoundsAndFirstCalendarDayAreNotChanged()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 50)); using var f = new Fixture(request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("transfer-suggestions", StringComparison.Ordinal)) { Assert.Equal(text, Query(request)["q"]); return Json(new { suggestions = Array.Empty<string>(), hasMore = false }); }
            Assert.Equal(text, Query(request)["name"]); Assert.Equal("0001-01-01", Query(request)["transactionDate"]); return Json(new { fromPaymentMethodId = (Guid?)null, toPaymentMethodId = (Guid?)null });
        });
        await f.Workspace.SuggestAsync(new(text)); await f.Workspace.AutofillAsync(new(text, DateOnly.MinValue));
        Assert.Throws<TransferInputException>(() => new TransferSuggestionQuery(text + "a")); Assert.Throws<TransferInputException>(() => new TransferAutofillQuery(text + "a", DateOnly.MinValue)); Assert.Equal(2, f.Requests.Count);
    }
}
