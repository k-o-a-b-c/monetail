using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class LedgerMasterCacheTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Mine = Guid.NewGuid(), Other = Guid.NewGuid(), Cash = Guid.NewGuid(), OtherCash = Guid.NewGuid();
    private static PeopleInfo People(string name = "花子") => new([new(Mine, "本人", null, null, 1, Guid.NewGuid()), new(Other, name, new(2000, 1, 1), new(2000, 1, 2), 0, Guid.NewGuid())], Mine, "Admin", Guid.NewGuid());
    private static PaymentMethodsInfo Payments() => new([new(Cash, Mine, "現金", 0, null, null, Guid.NewGuid()), new(OtherCash, Other, "現金", 0, new(2000, 1, 1), new(2000, 1, 2), Guid.NewGuid())], [new(Mine, Guid.NewGuid()), new(Other, Guid.NewGuid())], Mine, "Admin");
    [Fact]
    public async Task CacheKeepsAllMastersAndReusesFirstLoadWithoutFilteringOrMutatingNames()
    {
        var cache = new LedgerMasterCache(); var reads = 0; var people = People(); var payments = Payments(); var categories = new CategoriesInfo([new(Guid.NewGuid(), "科目", "Expense", "Standard", null, new(2000, 1, 1), 0, Guid.NewGuid())], Guid.NewGuid(), "Admin");
        await cache.GetPeopleAsync(Ledger, _ => { reads++; return Task.FromResult(people); });
        await cache.GetPaymentsAsync(Ledger, _ => { reads++; return Task.FromResult(payments); });
        await cache.GetCategoriesAsync(Ledger, _ => { reads++; return Task.FromResult(categories); });
        await cache.GetPeopleAsync(Ledger, _ => throw new Exception()); await cache.GetPaymentsAsync(Ledger, _ => throw new Exception()); await cache.GetCategoriesAsync(Ledger, _ => throw new Exception());
        Assert.Equal(3, reads); Assert.Same(people, cache.People(Ledger)); Assert.Same(payments, cache.Payments(Ledger)); Assert.Same(categories, cache.Categories(Ledger));
        Assert.Equal("現金", cache.PaymentName(Ledger, Cash)); Assert.Equal("（花子）現金", cache.PaymentName(Ledger, OtherCash));
        cache.UpdateIdentity(Ledger, null); Assert.Equal("（本人）現金", cache.PaymentName(Ledger, Cash)); Assert.Equal("現金", payments.PaymentMethods[0].Name);
        cache.UpdateIdentity(Ledger, Other); Assert.Equal("現金", cache.PaymentName(Ledger, OtherCash)); Assert.Equal("（本人）現金", cache.PaymentName(Ledger, Cash));
    }
    [Fact]
    public async Task PendingLoadsAreSharedAndManagementUpdateWinsOverOlderRead()
    {
        var cache = new LedgerMasterCache(); var pending = new TaskCompletionSource<PeopleInfo>(); var reads = 0;
        var first = cache.GetPeopleAsync(Ledger, _ => { reads++; return pending.Task; });
        var second = cache.GetPeopleAsync(Ledger, _ => throw new Exception());
        var updated = People("新しい名前"); cache.UpdatePeople(Ledger, updated); pending.SetResult(People("古い名前"));
        Assert.Same(updated, await first); Assert.Same(updated, await second); Assert.Equal(1, reads); Assert.Same(updated, cache.People(Ledger));
    }
    [Fact]
    public async Task ContextClearDiscardsAllMastersAndIgnoredCancellationCannotRestoreThem()
    {
        var cache = new LedgerMasterCache(); var pending = new TaskCompletionSource<PeopleInfo>();
        var read = cache.GetPeopleAsync(Ledger, _ => pending.Task); cache.UpdatePayments(Ledger, Payments()); cache.UpdateCategories(Ledger, new([], Guid.NewGuid(), "Admin"));
        cache.Clear(); pending.SetResult(People()); await read;
        Assert.Null(cache.People(Ledger)); Assert.Null(cache.Payments(Ledger)); Assert.Null(cache.Categories(Ledger)); Assert.Null(cache.MyPersonId(Ledger));
        var newLedger = Guid.NewGuid(); cache.UpdatePeople(newLedger, People()); Assert.Null(cache.People(Ledger)); Assert.NotNull(cache.People(newLedger));
    }
    [Fact]
    public async Task ExplicitPersonMappingChangeIsNotUndoneByPendingPaymentRead()
    {
        var cache = new LedgerMasterCache(); cache.UpdatePeople(Ledger, People()); var pending = new TaskCompletionSource<PaymentMethodsInfo>();
        var read = cache.GetPaymentsAsync(Ledger, _ => pending.Task); cache.UpdateIdentity(Ledger, Other);
        pending.SetResult(Payments()); await read; Assert.Equal(Other, cache.MyPersonId(Ledger)); Assert.Equal("現金", cache.PaymentName(Ledger, OtherCash));
    }
    [Fact]
    public async Task FailedInitialLoadCanBeRetriedAndUpdatedNamesAreVisibleWithoutAnotherRead()
    {
        var cache = new LedgerMasterCache(); await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetPeopleAsync(Ledger, _ => Task.FromException<PeopleInfo>(new HttpRequestException())));
        await cache.GetPeopleAsync(Ledger, _ => Task.FromResult(People())); cache.UpdatePayments(Ledger, Payments());
        cache.UpdatePeople(Ledger, People("変更後")); Assert.Equal("（変更後）現金", cache.PaymentName(Ledger, OtherCash));
        cache.UpdatePayments(Ledger, Payments() with { PaymentMethods = [Payments().PaymentMethods[1] with { Name = "カード" }] }); Assert.Equal("（変更後）カード", cache.PaymentName(Ledger, OtherCash));
    }
}
