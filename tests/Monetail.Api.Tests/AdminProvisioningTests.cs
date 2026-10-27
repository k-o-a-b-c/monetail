using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>初期管理者と登録コードの保存・再実行・競合・失敗時の保持を検証する。</summary>
public class AdminProvisioningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BootstrapPreservesBusinessRowsAndStoresOnlyHashWithTenMinuteExpiry()
    {
        using var fixture = new AuthDatabaseFixture();
        var tables = fixture.Tables();
        var before = fixture.Snapshot(tables);
        await fixture.MigrateAsync();
        var logs = new CapturedLogs();
        await using var services = Services(fixture, logs);
        await using var scope = services.CreateAsyncScope();
        var issued = Assert.IsType<IssuedEnrollmentCode>(await scope.ServiceProvider
            .GetRequiredService<AdminProvisioningService>().InitializeAdminAsync());
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var stored = await context.EnrollmentCodes.AsNoTracking().SingleAsync();
        Assert.Equal(32, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(issued.Code).Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(issued.Code)), stored.CodeHash);
        Assert.Equal(Now.UtcDateTime, stored.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddMinutes(10), stored.ExpiresAtUtc);
        Assert.Equal(stored.ExpiresAtUtc, issued.ExpiresAtUtc);
        Assert.Equal("passkey-enrollment", stored.Purpose);
        Assert.Null(stored.ConsumedAtUtc);
        Assert.Null(stored.RevokedAtUtc);
        var user = await context.Users.SingleAsync();
        Assert.False(user.IsEnabled);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.Email);
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>().IsInRoleAsync(user, "Admin"));
        Assert.Empty(await context.UserLinks.ToArrayAsync());
        Assert.Null(user.DisplayName);
        Assert.Equal(before, fixture.Snapshot(tables));
        Assert.DoesNotContain(issued.Code, fixture.Snapshot(fixture.Tables()));
        Assert.DoesNotContain(issued.Code, string.Join("\n", logs.Messages));
        Assert.DoesNotContain(issued.Code, issued.ToString());
        Assert.Contains(logs.Messages, line => line.Contains("初期管理者を作成"));
    }

    [Fact]
    public async Task RepeatedBootstrapDoesNotRedisplayRotateOrDuplicateAnything()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await Bootstrap(fixture);
        var before = fixture.Snapshot(fixture.Tables());
        Assert.Null(await Bootstrap(fixture));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
    }

    [Fact]
    public async Task ExplicitReissueRevokesOldCodeAndPreservesAccountRolesAndBusinessRows()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var old = Assert.IsType<IssuedEnrollmentCode>(await Bootstrap(fixture));
        var protectedTables = fixture.Tables().Where(table => table != "auth_enrollment_codes").ToArray();
        var before = fixture.Snapshot(protectedTables);
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync((string)fixture.Scalar("SELECT Id FROM AspNetUsers")!);
        Assert.Equal(old.AuthUserId, issued.AuthUserId);
        Assert.NotEqual(old.Code, issued.Code);
        var codes = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().EnrollmentCodes.AsNoTracking().ToListAsync();
        Assert.Equal(2, codes.Count);
        Assert.Single(codes, code => code.RevokedAtUtc == null && code.ConsumedAtUtc == null);
        Assert.Equal(Now.UtcDateTime, codes.Single(code => code.CodeHash.SequenceEqual(
            SHA256.HashData(Encoding.UTF8.GetBytes(old.Code)))).RevokedAtUtc);
        Assert.Equal(before, fixture.Snapshot(protectedTables));
    }

    [Fact]
    public async Task BootstrapFailureRollsBackAccountRoleAndCodeThenAllowsRetry()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        fixture.Execute("CREATE TRIGGER reject_code BEFORE INSERT ON auth_enrollment_codes BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = fixture.Snapshot(fixture.Tables());
        await Assert.ThrowsAsync<DbUpdateException>(() => Bootstrap(fixture));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
        fixture.Execute("DROP TRIGGER reject_code");
        Assert.NotNull(await Bootstrap(fixture));
    }

    [Fact]
    public async Task ReissueFailurePreservesPreviouslyUsableCode()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await Bootstrap(fixture);
        fixture.Execute("CREATE TRIGGER reject_code BEFORE INSERT ON auth_enrollment_codes BEGIN SELECT RAISE(ABORT,'injected'); END");
        var before = fixture.Snapshot(fixture.Tables());
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider
            .GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync((string)fixture.Scalar("SELECT Id FROM AspNetUsers")!));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReissueRejectsEnabledAccountOrRemainingPasskeyWithoutChanges(bool enabled)
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await Bootstrap(fixture);
        await using var services = Services(fixture);
        await using (var setup = services.CreateAsyncScope())
        {
            var users = setup.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = (await users.GetUsersInRoleAsync("Admin")).Single();
            if (enabled)
            {
                user.IsEnabled = true;
                Assert.True((await users.UpdateAsync(user)).Succeeded);
            }
            else
            {
                var key = new UserPasskeyInfo([1, 2, 3], [4, 5], Now, 0, ["internal"], true, true, false, [6], [7]);
                Assert.True((await users.AddOrUpdatePasskeyAsync(user, key)).Succeeded);
            }
        }
        var before = fixture.Snapshot(fixture.Tables());
        await using var scope = services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<AdminProvisioningService>().IssueEnrollmentCodeAsync((string)fixture.Scalar("SELECT Id FROM AspNetUsers")!));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
    }

    [Fact]
    public async Task UnknownAccountAndUnmigratedDatabaseAreRejected()
    {
        using var fixture = new AuthDatabaseFixture();
        var original = fixture.Snapshot(fixture.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(fixture));
        Assert.Equal(original, fixture.Snapshot(fixture.Tables()));
        await fixture.MigrateAsync();
        var before = fixture.Snapshot(fixture.Tables());
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueEnrollmentCodeAsync("missing"));
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
        fixture.Execute("DELETE FROM m_user");
        var issued = Assert.IsType<IssuedEnrollmentCode>(await Bootstrap(fixture));
        Assert.Equal(issued.AuthUserId, (await service.IssueEnrollmentCodeAsync(issued.AuthUserId)).AuthUserId);
    }

    [Fact]
    public async Task ExistingGeneralAccountIsNotPromotedByBootstrap()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var general = new AuthUser { UserName = "general" };
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        Assert.True((await users.CreateAsync(general)).Succeeded);
        var issued = Assert.IsType<IssuedEnrollmentCode>(await Bootstrap(fixture));
        Assert.NotEqual(general.Id, issued.AuthUserId);
        Assert.Empty(await users.GetRolesAsync(general));
        Assert.False((await users.FindByIdAsync(general.Id))!.IsEnabled);
        Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM auth_user_links"));
    }

    [Fact]
    public async Task ConcurrentInitializationsCreateOnlyOneAccountAndOneCode()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var results = await Task.WhenAll(Task.Run(() => Bootstrap(fixture)), Task.Run(() => Bootstrap(fixture)));
        Assert.Single(results, result => result != null);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM auth_user_links"));
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes"));
    }

    /// <summary>本番と同じDI構成に、時刻とログの観測だけを追加する。</summary>
    private static ServiceProvider Services(AuthDatabaseFixture fixture, CapturedLogs? logs = null)
    {
        var services = new ServiceCollection();
        services.AddAuthDatabase(fixture.ConnectionString);
        services.AddSingleton<TimeProvider>(new FixedTime());
        if (logs != null) services.AddLogging(builder => builder.AddProvider(logs));
        return services.BuildServiceProvider();
    }

    /// <summary>CLIと同様に独立したスコープで初期作成する。</summary>
    private static async Task<IssuedEnrollmentCode?> Bootstrap(AuthDatabaseFixture fixture)
    {
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync();
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>通常のログへ生コードが含まれないことを観測する。</summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }
        private sealed class CaptureLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error,
                Func<TState, Exception?, string> formatter) => owner.Messages.Add(formatter(state, error));
        }
    }
}
