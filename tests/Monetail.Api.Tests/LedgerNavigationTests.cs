using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Monetail.Api.Features.Monetail.Ledgers;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class LedgerNavigationTests
{
    [Fact]
    public async Task NoHistoryAndListingNeverSaveASelection()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        await CreateAsync(client, "家計簿");
        var before = app.Database.Snapshot(app.Database.Tables());
        Assert.Null((await client.GetFromJsonAsync<JsonElement>("/ledgers/last-opened")).GetProperty("ledger").GetString());
        await client.GetAsync("/ledgers");
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_navigation"));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    [InlineData("Admin")]
    public async Task SelectionIsAccountScopedSharedBetweenSessionsAndUsesCurrentRole(string role)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = await LoginAsync(app);
        var ledger = await CreateAsync(first, "家計簿");
        app.Database.Execute($"UPDATE ledger_members SET role='{role}' WHERE ledger_id='{ledger.LedgerId}'");
        using var saved = await first.PostAsync($"/ledgers/{ledger.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var second = await LoginAsync(app);
        var response = await second.GetFromJsonAsync<JsonElement>("/ledgers/last-opened");
        Assert.Equal(ledger.LedgerId, response.GetProperty("ledger").GetProperty("ledgerId").GetString());
        Assert.Equal(role, response.GetProperty("ledger").GetProperty("role").GetString());
        var before = app.Database.Snapshot(app.Database.Tables());
        using var repeated = await second.PostAsync($"/ledgers/{ledger.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var other = await LoginAsync(app, await app.AddAccountAsync());
        Assert.Equal(JsonValueKind.Null, (await other.GetFromJsonAsync<JsonElement>("/ledgers/last-opened")).GetProperty("ledger").ValueKind);
    }

    [Fact]
    public async Task UnavailableHistoryIsNotReturnedOrDeletedAndAnotherSelectionCanReplaceIt()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var old = await CreateAsync(client, "前回");
        var next = await CreateAsync(client, "今回");
        await client.PostAsync($"/ledgers/{old.LedgerId}/open", null);
        app.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{old.LedgerId}'");
        var before = app.Database.Snapshot(app.Database.Tables());
        var response = await client.GetFromJsonAsync<JsonElement>("/ledgers/last-opened");
        Assert.Equal(JsonValueKind.Null, response.GetProperty("ledger").ValueKind);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        using var saved = await client.PostAsync($"/ledgers/{next.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(next.LedgerId, app.Database.Scalar("SELECT last_opened_ledger_id FROM ledger_navigation"));
    }

    [Fact]
    public async Task UnknownAndUnjoinedLedgersHaveSameRejectionAndNeverChangeHistory()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var mine = await CreateAsync(client, "自分");
        await client.PostAsync($"/ledgers/{mine.LedgerId}/open", null);
        using var other = await LoginAsync(app, await app.AddAccountAsync());
        var hidden = await CreateAsync(other, "秘密");
        var before = app.Database.Snapshot(app.Database.Tables());
        foreach (var id in new[] { hidden.LedgerId, Guid.NewGuid().ToString("N") })
        {
            using var response = await client.PostAsync($"/ledgers/{id}/open", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task BodyCannotSelectAnotherAccountOrLedger()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var mine = await CreateAsync(client, "自分");
        var otherAccount = await app.AddAccountAsync();
        using var other = await LoginAsync(app, otherAccount);
        var hidden = await CreateAsync(other, "秘密");
        using var response = await client.PostAsJsonAsync($"/ledgers/{mine.LedgerId}/open",
            new { authUserId = otherAccount.UserId, ledgerId = hidden.LedgerId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mine, await response.Content.ReadFromJsonAsync<LedgerResponse>());
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_navigation"));
        Assert.Equal(app.UserId, app.Database.Scalar("SELECT auth_user_id FROM ledger_navigation"));
        Assert.Equal(mine.LedgerId, app.Database.Scalar("SELECT last_opened_ledger_id FROM ledger_navigation"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task AnonymousRequestsAreRejected(string method)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "GET" ? "/ledgers/last-opened" : $"/ledgers/{Guid.NewGuid():N}/open");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_navigation"));
    }

    [Theory]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("origin", HttpStatusCode.Forbidden)]
    [InlineData("large", HttpStatusCode.RequestEntityTooLarge)]
    public async Task SavingAppliesTheExistingCookieHttpBoundary(string invalid, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        if (invalid == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (invalid == "origin") { client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        var body = invalid == "large" ? new string('a', 70000) : JsonSerializer.Serialize(new { ledgerId = Guid.NewGuid() });
        using var response = await client.PostAsync($"/ledgers/{Guid.NewGuid():N}/open", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_navigation"));
    }

    [Theory]
    [InlineData("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId=NEW.auth_user_id", HttpStatusCode.Unauthorized)]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=0 WHERE Id=NEW.auth_user_id", HttpStatusCode.Unauthorized)]
    [InlineData("DELETE FROM ledger_members WHERE ledger_id=NEW.last_opened_ledger_id AND auth_user_id=NEW.auth_user_id", HttpStatusCode.NotFound)]
    [InlineData("SELECT RAISE(ABORT,'synthetic-private-error')", HttpStatusCode.ServiceUnavailable)]
    public async Task SaveFailuresAndLostAuthorizationRollBackTheWholeTransaction(string mutation, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var ledger = await CreateAsync(client, "家計簿");
        app.Database.Execute($"CREATE TRIGGER change_navigation AFTER INSERT ON ledger_navigation BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.PostAsync($"/ledgers/{ledger.LedgerId}/open", null);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("synthetic-private-error", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER change_navigation");
        using var retry = await client.PostAsync($"/ledgers/{ledger.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    private static async Task<HttpClient> LoginAsync(LoginTestApp app, (string UserId, byte[] CredentialId)? account = null)
    {
        var client = app.Client();
        var options = await app.BeginAsync(client);
        using var login = await app.VerifyAsync(client, account is { } user ? app.AssertionFor(options, user.UserId, user.CredentialId) : app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        return client;
    }

    [Fact]
    public async Task FailedSwitchKeepsThePreviousLedgerAndReturnsNoLedgerData()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var first = await CreateAsync(client, "前回");
        var next = await CreateAsync(client, "今回");
        using var opened = await client.PostAsync($"/ledgers/{first.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        app.Database.Execute("CREATE TRIGGER fail_switch BEFORE UPDATE ON ledger_navigation BEGIN SELECT RAISE(ABORT,'synthetic-private-error'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var failed = await client.PostAsync($"/ledgers/{next.LedgerId}/open", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var body = await failed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(next.LedgerId, body);
        Assert.DoesNotContain(next.Name, body);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task ConcurrentOpensKeepOneAccountRowAndEitherCompletedSelection()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var first = await CreateAsync(client, "家計簿A");
        var second = await CreateAsync(client, "家計簿B");
        var responses = await Task.WhenAll(client.PostAsync($"/ledgers/{first.LedgerId}/open", null),
            client.PostAsync($"/ledgers/{second.LedgerId}/open", null));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_navigation"));
            Assert.Contains(app.Database.Scalar("SELECT last_opened_ledger_id FROM ledger_navigation"), new object[] { first.LedgerId, second.LedgerId });
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static async Task<LedgerResponse> CreateAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/ledgers", new { name, requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LedgerResponse>())!;
    }
}
