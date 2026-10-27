using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Accounts;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>一般アカウントの初回発行・永続的な再送識別・権限・保存の原子性を検証する。</summary>
public class AdminAccountCreationTests
{
    [Fact]
    public async Task CreationPreservesExistingDataAndReturnsAHashOnlyCodeThatCanEnrollAnOrdinaryAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var tables = app.Database.Tables().Except(new[] { "AspNetUsers", "auth_enrollment_codes", "auth_account_creation_requests" }).ToArray();
        var before = app.Database.Snapshot(tables);
        using var created = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.False(created.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "authUserId", "code", "expiresAtUtc" }, body.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var id = body.GetProperty("authUserId").GetString()!;
        var code = body.GetProperty("code").GetString()!;
        Assert.False(body.TryGetProperty("userCode", out _));
        Assert.Equal(43, code.Length);
        Assert.Equal(app.Time.GetUtcNow().AddMinutes(10), body.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.Equal(0L, app.Database.Scalar($"SELECT IsEnabled FROM AspNetUsers WHERE Id='{id}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUserRoles WHERE UserId='{id}'"));
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM AspNetUsers WHERE Id='{id}' AND (PasswordHash IS NOT NULL OR Email IS NOT NULL)"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        Assert.Equal(1L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_enrollment_codes WHERE CodeHash=X'{hash}' AND AuthUserId='{id}'"));
        Assert.DoesNotContain(code, app.Database.Snapshot(app.Database.Tables()));
        Assert.DoesNotContain(code, new AccountCreationResponse(id, code, null).ToString());
        Assert.Equal(before, app.Database.Snapshot(tables));
        using var enrollee = app.Client();
        await LoginTestApp.CsrfAsync(enrollee);
        using var begin = await enrollee.PostAsJsonAsync("/auth/enrollment/options", new { code, displayName = "新しいアカウント名" });
        var options = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var key = RandomNumberGenerator.GetBytes(32);
        using var enrolled = await enrollee.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, key) });
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        using var notLoggedIn = await enrollee.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, notLoggedIn.StatusCode);
        options = await app.BeginAsync(enrollee);
        using var loggedIn = await app.VerifyAsync(enrollee, app.AssertionFor(options, id, key));
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var me = await enrollee.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.False(me.TryGetProperty("userCode", out _));
        Assert.Equal("新しいアカウント名", me.GetProperty("displayName").GetString());
        Assert.Equal(0L, app.Database.Scalar($"SELECT COUNT(*) FROM auth_user_links WHERE auth_user_id='{id}'"));
        await LoginTestApp.CsrfAsync(enrollee);
        using var denied = await CreateAsync(enrollee, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task RetryAfterRestartReturnsTheSameAccountWithoutShowingOrReissuingTheCode()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var request = Guid.NewGuid();
        using var first = await CreateAsync(admin, request);
        var original = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = original.GetProperty("authUserId").GetString()!;
        app.Database.Execute($"UPDATE auth_enrollment_codes SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId='{id}'");
        var before = app.Database.Snapshot(app.Database.Tables());
        await using var restarted = app.NewFactory();
        using var retry = app.Client(restarted);
        retry.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(retry);
        using var response = await CreateAsync(retry, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("authUserId").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("code").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expiresAtUtc").ValueKind);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequestsAreDeduplicatedOnlyForTheSameRequestId(bool sameRequest)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var request = Guid.NewGuid();
        var responses = await Task.WhenAll(CreateAsync(admin, request), CreateAsync(admin, sameRequest ? request : Guid.NewGuid()));
        try
        {
            Assert.Equal(sameRequest ? 1 : 2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            if (sameRequest) Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
            Assert.Equal(sameRequest ? 1 : 2, bodies.Count(body => JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String));
            Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM auth_user_links WHERE user_code=3"));
            Assert.Equal(sameRequest ? 1L : 2L, app.Database.Scalar("SELECT COUNT(*) FROM auth_account_creation_requests"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task RequestIsBoundToItsOriginalAdministratorAndDoesNotRevealTheCodeToAnother()
    {
        await using var app = await LoginTestApp.CreateAsync();
        var other = await app.AddAccountAsync(2);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(other.UserId))!, "Admin")).Succeeded);
        }
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var request = Guid.NewGuid();
        using var first = await CreateAsync(admin, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var another = app.Client();
        var options = await app.BeginAsync(another);
        using var login = await app.VerifyAsync(another, app.AssertionFor(options, other.UserId, other.CredentialId));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(another);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var wrongAdmin = await CreateAsync(another, request);
        Assert.Equal(HttpStatusCode.Conflict, wrongAdmin.StatusCode);
        Assert.False((await wrongAdmin.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out _));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("role", HttpStatusCode.Forbidden)]
    [InlineData("stale", HttpStatusCode.Forbidden)]
    [InlineData("future", HttpStatusCode.Forbidden)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    public async Task InvalidAdministratorStateCannotCreateAnything(string change, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        if (change != "anonymous") await LoginAsync(app, admin);
        else await LoginTestApp.CsrfAsync(admin);
        switch (change)
        {
            case "role": app.Database.Execute("DELETE FROM AspNetUserRoles"); break;
            case "stale": app.Time.Advance(TimeSpan.FromMinutes(5)); break;
            case "future": app.Database.Execute("UPDATE auth_sessions SET VerifiedAtUtc=ExpiresAtUtc"); break;
            case "disabled": app.Database.Execute("UPDATE AspNetUsers SET IsEnabled=0"); break;
            case "revoked": app.Database.Execute("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"); break;
            case "expired": app.Database.Execute("UPDATE auth_sessions SET ExpiresAtUtc=CreatedAtUtc"); break;
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("AspNetUsers")]
    [InlineData("auth_enrollment_codes")]
    [InlineData("auth_account_creation_requests")]
    public async Task EveryWriteFailureRollsBackTheWholeCreationAndAllowsTheSameRequestToRetry(string table)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        app.Database.Execute($"CREATE TRIGGER reject_creation BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'synthetic-private-error'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        var request = Guid.NewGuid();
        using var failed = await CreateAsync(admin, request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("synthetic-private-error", await failed.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER reject_creation");
        using var retried = await CreateAsync(admin, request);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    [Theory]
    [InlineData("DELETE FROM AspNetUserRoles WHERE UserId=NEW.CreatedByAuthUserId")]
    [InlineData("UPDATE AspNetUsers SET IsEnabled=0 WHERE Id=NEW.CreatedByAuthUserId")]
    [InlineData("UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc WHERE AuthUserId=NEW.CreatedByAuthUserId")]
    public async Task AdministratorChangesDuringSavingRollBackAllWrites(string mutation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        app.Database.Execute($"CREATE TRIGGER change_actor AFTER INSERT ON auth_account_creation_requests BEGIN {mutation}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task VerificationExpiringDuringSavingRollsBackTheNewAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = app.Client();
        await LoginAsync(app, owner);
        await using var delayed = app.NewFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AuthDbContext>(options => options.AddInterceptors(new DelayCreation(app.Time)))));
        using var admin = app.Client(delayed);
        admin.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(admin);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requestId\":\"00000000-0000-0000-0000-000000000000\",\"userCode\":3}")]
    [InlineData("{\"requestId\":\"invalid\",\"userCode\":3}")]
    [InlineData("{\"requestId\":\"11111111-1111-1111-1111-111111111111\",\"userCode\":3,\"role\":\"Admin\"}")]
    [InlineData("{\"requestId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"新規人物\"}")]
    public async Task MalformedOrUnsupportedRequestsDoNotChangeAnything(string body)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await admin.PostAsync("/admin/accounts", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task CsrfOriginBodyLimitAndSharedRateLimitProtectCreation()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        var before = app.Database.Snapshot(app.Database.Tables());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrf = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        await LoginTestApp.CsrfAsync(admin);
        admin.DefaultRequestHeaders.Remove("Origin");
        admin.DefaultRequestHeaders.Add("Origin", "https://other.example.test");
        using var origin = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        admin.DefaultRequestHeaders.Remove("Origin");
        admin.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        using var oversized = await admin.PostAsync("/admin/accounts", new StringContent(new string(' ', 65537), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await using var isolated = app.NewFactory();
        using var limited = app.Client(isolated);
        limited.DefaultRequestHeaders.Add("Cookie", app.SessionCookie.Split(';')[0]);
        await LoginTestApp.CsrfAsync(limited);
        for (var index = 0; index < 20; index++)
        {
            using var response = await CreateAsync(limited, Guid.Empty);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var throttled = await limited.PostAsJsonAsync("/auth/login/options", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task MissingExplicitMigrationIsRejectedWithoutCreatingTheTable()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var admin = app.Client();
        await LoginAsync(app, admin);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().GetService<IMigrator>().MigrateAsync("20261025080829_AddSessions");
        var before = app.Database.Snapshot(app.Database.Tables());
        using var result = await CreateAsync(admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        Assert.DoesNotContain("auth_account_creation_requests", app.Database.Tables());
    }

    /// <summary>管理者としてログインし、その本人に対応するCSRFトークンを取得する。</summary>
    private static async Task LoginAsync(LoginTestApp app, HttpClient client)
    {
        await app.LoginAsync(client);
        await LoginTestApp.CsrfAsync(client);
    }

    /// <summary>再送識別子だけを一般アカウント発行APIへ送る。</summary>
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid requestId) =>
        client.PostAsJsonAsync("/admin/accounts", new { requestId });

    /// <summary>要求記録の保存中に本人確認の有効期間を超過させる。</summary>
    private sealed class DelayCreation(LoginTestApp.LoginTime time) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"auth_account_creation_requests\"", StringComparison.Ordinal))
                time.Advance(TimeSpan.FromMinutes(5));
            return base.ReaderExecutingAsync(command, eventData, result, token);
        }
    }
}
