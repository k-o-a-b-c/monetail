using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>家計簿の作成・再送・参加範囲・認証と原子的保存を実HTTPとSQLiteで検証する。</summary>
public class LedgerApiTests
{
    [Fact]
    public async Task OrdinaryAccountCanCreateSameNamedLedgersWithoutRecentVerificationOrPeople()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var account = await app.AddAccountAsync();
        using var client = await LoginAsync(app, account);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var preserved = app.Database.Tables().Where(table => !table.StartsWith("ledger", StringComparison.Ordinal)).ToArray();
        var before = app.Database.Snapshot(preserved);
        using var first = await CreateAsync(client, "  同じ名前  ");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var ledger = (await first.Content.ReadFromJsonAsync<LedgerResponse>())!;
        Assert.Equal("同じ名前", ledger.Name);
        Assert.Equal("Admin", ledger.Role);
        Assert.True(Guid.TryParse(ledger.LedgerId, out _));
        Assert.True(Guid.TryParse(ledger.Revision, out _));
        Assert.Equal($"/ledgers/{ledger.LedgerId}", first.Headers.Location!.OriginalString);
        using var second = await CreateAsync(client, "同じ名前");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(ledger.LedgerId, (await second.Content.ReadFromJsonAsync<LedgerResponse>())!.LedgerId);
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM ledgers"));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members WHERE role='Admin'"));
        Assert.Equal(account.UserId, app.Database.Scalar($"SELECT created_by_auth_user_id FROM ledgers WHERE id='{ledger.LedgerId}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUserRoles WHERE UserId='{account.UserId}'"));
        Assert.DoesNotContain("ledger_people", app.Database.Tables());
        Assert.DoesNotContain("m_payment_classification", app.Database.Tables());
        Assert.Equal(before, app.Database.Snapshot(preserved));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("spaces")]
    [InlineData("long")]
    [InlineData("surrogate")]
    [InlineData("missing-request")]
    [InlineData("bad-request")]
    [InlineData("role")]
    [InlineData("creator")]
    public async Task InvalidNamesRequestsAndClientSuppliedOwnershipNeverWrite(string kind)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var data = new Dictionary<string, object?> { ["name"] = "家計簿", ["requestId"] = Guid.NewGuid() };
        switch (kind)
        {
            case "null": data["name"] = null; break;
            case "empty": data["name"] = ""; break;
            case "spaces": data["name"] = " \t "; break;
            case "long": data["name"] = new string('あ', 101); break;
            case "surrogate": data["name"] = string.Concat(Enumerable.Repeat("😀", 51)); break;
            case "missing-request": data.Remove("requestId"); break;
            case "bad-request": data["requestId"] = "invalid"; break;
            case "role": data["role"] = "Admin"; break;
            case "creator": data["createdByAuthUserId"] = "another-account"; break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.PostAsJsonAsync("/ledgers", data);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task MaximumNameLengthAndNormalizedReplayAreAccepted()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var request = Guid.NewGuid();
        var name = new string('あ', 100);
        using var first = await CreateAsync(client, $"  {name}  ", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<LedgerResponse>();
        var before = app.Database.Snapshot(app.Database.Tables());
        using var replay = await CreateAsync(client, name, request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadFromJsonAsync<LedgerResponse>());
        using var changed = await CreateAsync(client, "異なる名前", request);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("GET", "/ledgers")]
    [InlineData("GET", "/ledgers/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/ledgers")]
    public async Task AnonymousRequestsAreRejectedWithoutWrites(string method, string path)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        var before = app.Database.Snapshot(app.Database.Tables());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { name = "家計簿", requestId = Guid.NewGuid() });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("origin", HttpStatusCode.Forbidden)]
    [InlineData("http", HttpStatusCode.BadRequest)]
    [InlineData("host", HttpStatusCode.BadRequest)]
    [InlineData("large", HttpStatusCode.RequestEntityTooLarge)]
    public async Task CreationUsesTheSameHttpBoundaryAsAuthentication(string invalid, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var before = app.Database.Snapshot(app.Database.Tables());
        if (invalid == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (invalid == "origin") { client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        var address = invalid switch { "http" => "http://monetail.example.test/ledgers", "host" => "https://other.example.test/ledgers", _ => "/ledgers" };
        using var body = new StringContent(JsonSerializer.Serialize(new { name = invalid == "large" ? new string('a', 70000) : "家計簿", requestId = Guid.NewGuid() }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(address, body);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    [InlineData("Admin")]
    public async Task ReadsUseCurrentMembershipAndGlobalAdminHasNoBypass(string role)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var owner = await app.AddAccountAsync();
        using var ownerClient = await LoginAsync(app, owner);
        using var created = await CreateAsync(ownerClient, "別の人の家計簿");
        var ledger = (await created.Content.ReadFromJsonAsync<LedgerResponse>())!;
        using var admin = await LoginAsync(app);
        Assert.Empty((await admin.GetFromJsonAsync<LedgerResponse[]>("/ledgers"))!);
        using var hidden = await admin.GetAsync($"/ledgers/{ledger.LedgerId}");
        using var absent = await admin.GetAsync($"/ledgers/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(hidden.StatusCode, absent.StatusCode);
        Assert.Equal(await hidden.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());
        app.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{ledger.LedgerId}','{app.UserId}','{role}','2026-10-27','revision')");
        var before = app.Database.Snapshot(app.Database.Tables());
        var visible = Assert.Single((await admin.GetFromJsonAsync<LedgerResponse[]>("/ledgers"))!);
        Assert.Equal(role, visible.Role);
        using var details = await admin.GetAsync($"/ledgers/{ledger.LedgerId}");
        Assert.Equal(visible, await details.Content.ReadFromJsonAsync<LedgerResponse>());
        Assert.Equal("no-store", details.Headers.CacheControl!.ToString());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute($"DELETE FROM ledger_members WHERE ledger_id='{ledger.LedgerId}' AND auth_user_id='{app.UserId}'");
        using var removed = await admin.GetAsync($"/ledgers/{ledger.LedgerId}");
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayDoesNotRestoreRemovedMembershipOrManagementRole(bool removed)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var request = Guid.NewGuid();
        using var created = await CreateAsync(client, "家計簿", request);
        var ledger = (await created.Content.ReadFromJsonAsync<LedgerResponse>())!;
        app.Database.Execute(removed ? "DELETE FROM ledger_members" : "UPDATE ledger_members SET role='Viewer'");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var replay = await CreateAsync(client, "家計簿", request);
        Assert.Equal(removed ? HttpStatusCode.NotFound : HttpStatusCode.OK, replay.StatusCode);
        if (!removed) Assert.Equal("Viewer", (await replay.Content.ReadFromJsonAsync<LedgerResponse>())!.Role);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task AnotherAccountCannotReplayTheSameRequestOrSeeItsResult()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var first = await LoginAsync(app);
        var request = Guid.NewGuid();
        using var created = await CreateAsync(first, "非公開の名前", request);
        var ledger = (await created.Content.ReadFromJsonAsync<LedgerResponse>())!;
        using var other = await LoginAsync(app, await app.AddAccountAsync());
        var before = app.Database.Snapshot(app.Database.Tables());
        using var replay = await CreateAsync(other, "非公開の名前", request);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        var body = await replay.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ledger.LedgerId, body);
        Assert.DoesNotContain(ledger.Name, body);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequestsPreserveAtomicCreationAndReplay(bool sameRequest)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var request = Guid.NewGuid();
        var responses = await Task.WhenAll(CreateAsync(client, "同名", request), CreateAsync(client, "同名", sameRequest ? request : Guid.NewGuid()));
        try
        {
            Assert.Equal(sameRequest ? 1 : 2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(sameRequest ? 1 : 0, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            foreach (var table in new[] { "ledgers", "ledger_members", "ledger_creation_requests" })
                Assert.Equal(sameRequest ? 1L : 2L, app.Database.Scalar($"SELECT COUNT(*) FROM {table}"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("ledgers")]
    [InlineData("ledger_members")]
    [InlineData("ledger_creation_requests")]
    [InlineData("ledger_navigation")]
    public async Task EveryWriteFailureRollsBackAllRowsAndAllowsRetry(string table)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        app.Database.Execute($"CREATE TRIGGER fail_ledger BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'synthetic-private-error'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        var request = Guid.NewGuid();
        using var failed = await CreateAsync(client, "家計簿", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("synthetic-private-error", await failed.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_ledger");
        using var retry = await CreateAsync(client, "家計簿", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=0 WHERE Id=NEW.actor_auth_user_id")]
    [InlineData("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId=NEW.actor_auth_user_id")]
    [InlineData("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc WHERE AuthUserId=NEW.actor_auth_user_id")]
    public async Task AuthenticationChangesDuringSavingRollBackAllWrites(string mutation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        app.Database.Execute($"CREATE TRIGGER change_ledger_actor AFTER INSERT ON ledger_creation_requests BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(client, "家計簿");
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task LosingCreatorAdminDuringSavingDoesNotLeaveAnUnmanagedLedger()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        app.Database.Execute("CREATE TRIGGER change_creator AFTER INSERT ON ledger_members BEGIN UPDATE ledger_members SET role='Viewer' WHERE ledger_id=NEW.ledger_id AND auth_user_id=NEW.auth_user_id; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(client, "家計簿");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task StoreFailureReturnsNoDataAndNeverRecreatesTheMissingTable()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        app.Database.Execute("DROP TABLE ledger_members");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await client.GetAsync("/ledgers");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task MembershipAndRequestConstraintsAreEnforcedBySqlite()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        using var created = await CreateAsync(client, "家計簿");
        var ledger = (await created.Content.ReadFromJsonAsync<LedgerResponse>())!;
        using var connection = app.Database.OpenForeignKeyConnection();
        var insert = $"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision) VALUES('{ledger.LedgerId}','{app.UserId}','Admin','2026-10-27','version')";
        foreach (var sql in new[]
        {
            insert,
            insert.Replace(ledger.LedgerId, "missing"),
            insert.Replace(app.UserId, "missing"),
            "UPDATE ledger_members SET role='Owner'",
            "UPDATE ledger_members SET role=NULL",
            "UPDATE ledger_creation_requests SET actor_auth_user_id='missing'",
            "UPDATE ledger_creation_requests SET ledger_id='missing'"
        }) Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection, sql)).SqliteErrorCode);
        await using var scope = app.Factory.Services.CreateAsyncScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.GetMigrations());
    }

    /// <summary>指定したアカウントでログインし、その状態のCSRFトークンを取得する。</summary>
    private static async Task<HttpClient> LoginAsync(LoginTestApp app, (string UserId, byte[] CredentialId)? account = null)
    {
        var client = app.Client();
        var options = await app.BeginAsync(client);
        using var login = await app.VerifyAsync(client, account is { } user ? app.AssertionFor(options, user.UserId, user.CredentialId) : app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        return client;
    }

    /// <summary>家計簿名と作成要求IDだけを送信する。</summary>
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, Guid? request = null) =>
        client.PostAsJsonAsync("/ledgers", new { name, requestId = request ?? Guid.NewGuid() });
}
