using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実プロセスの管理コマンドで、明示指定・非待受・全失効・再発行を検証する。</summary>
public class AuthenticationCommandTests
{
    [Fact]
    public async Task InitializeDatabaseRunsWithoutWebSettingsAndDoesNotIssueCodes()
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        var initialized = await RunAsync(fixture.ConnectionString, ["initialize-db"]);
        Assert.Equal(0, initialized.ExitCode);
        Assert.Contains("適用済み移行ID:", initialized.Output);
        Assert.DoesNotContain("登録コード:", initialized.Output);
        Assert.DoesNotContain("Now listening", initialized.Output);
        var first = await RunAsync(fixture.ConnectionString, ["initialize-admin"]);
        Assert.Equal(0, first.ExitCode);
        using var connection = fixture.OpenForeignKeyConnection();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT Id FROM AspNetUsers";
        var id = (string)query.ExecuteScalar()!;
        var repeated = await RunAsync(fixture.ConnectionString, ["initialize-admin"]);
        Assert.Equal(0, repeated.ExitCode);
        Assert.Contains(id, repeated.Output);
        Assert.DoesNotContain("登録コード:", repeated.Output);
        var initializedAgain = await RunAsync(fixture.ConnectionString, ["initialize-db"]);
        Assert.Equal(0, initializedAgain.ExitCode);
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("issue-enrollment-code")]
    public async Task UnknownCommandFailsWithoutStartingWebOrCreatingDatabase(string command)
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        var result = await RunAsync(fixture.ConnectionString, [command]);
        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(fixture.DatabasePath));
        Assert.DoesNotContain("Now listening", result.Output);
    }

    [Fact]
    public async Task InitializerRejectsWindowsDatabaseWithoutChangingSavedTotalsOrAnyOtherData()
    {
        using var fixture = new AuthDatabaseFixture(initializeWindowsDatabase: true);
        var tables = fixture.Tables();
        var before = fixture.Snapshot(tables);
        var result = await RunAsync(fixture.ConnectionString, ["initialize-db"]);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("登録コード:", result.Output);
        Assert.Equal(tables, fixture.Tables());
        Assert.Equal(before, fixture.Snapshot(tables));
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("invalid-target")]
    [InlineData("missing-restore-intent")]
    [InlineData("mixed-scope")]
    [InlineData("duplicate-target")]
    [InlineData("wrong-emergency-position")]
    [InlineData("unexpected-restore-flag")]
    public async Task InvalidOperationOptionsFailWithoutChangingDatabase(string usage)
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var arguments = usage switch
        {
            "missing-target" => new[] { "revoke-account" },
            "invalid-target" => ["revoke-account", "--account-id", "invalid"],
            "missing-restore-intent" => ["reset-authentication"],
            "mixed-scope" => ["reset-authentication", "--after-restore", "--account-id", "1"],
            "duplicate-target" => ["revoke-account", "--account-id", "1", "--account-id=other"],
            "wrong-emergency-position" => ["revoke-account", "--emergency-recovery", "--account-id", "1"],
            _ => ["revoke-account", "--account-id", "1", "--after-restore"]
        };
        var before = fixture.Snapshot(fixture.Tables());
        var result = await RunAsync(fixture.ConnectionString, arguments);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
        Assert.DoesNotContain("登録コード:", result.Output);
        Assert.DoesNotContain("Now listening", result.Output);
    }

    [Fact]
    public async Task InitializeAdminNeedsNoPeopleAndReexecutionDoesNotIssueAnotherCode()
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var tables = fixture.Tables().Where(table => !table.StartsWith("AspNet") && !table.StartsWith("auth_")).ToArray();
        var before = fixture.Snapshot(tables);
        var created = await RunAsync(fixture.ConnectionString, ["initialize-admin"]);
        Assert.Equal(0, created.ExitCode);
        Assert.StartsWith(LoginTestApp.Origin + "/auth/enroll#token=", created.StandardOutput);
        Assert.Equal(1, created.Output.Split('\n').Count(line => line.StartsWith(LoginTestApp.Origin + "/auth/enroll#token=", StringComparison.Ordinal)));
        Assert.DoesNotContain("Now listening", created.Output);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled=0 AND DisplayName IS NULL"));
        Assert.Equal(before, fixture.Snapshot(tables));
        var all = fixture.Tables();
        var createdState = fixture.Snapshot(all);
        var repeated = await RunAsync(fixture.ConnectionString, ["initialize-admin"]);
        Assert.Equal(0, repeated.ExitCode);
        Assert.DoesNotContain("登録コード:", repeated.Output);
        Assert.Equal(createdState, fixture.Snapshot(all));
    }

    [Fact]
    public async Task RecoveryCommandProtectsLastAdminThenExplicitlyRevokesAndAllowsSeparateCodeIssuance()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        var refused = await RunAsync(app.Database.ConnectionString, ["revoke-account", "--account-id", app.UserId]);
        Assert.Equal(1, refused.ExitCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        var codeCount = app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes");
        var revoked = await RunAsync(app.Database.ConnectionString, ["revoke-account", "--account-id", app.UserId, "--emergency-recovery"]);
        Assert.Equal(0, revoked.ExitCode);
        Assert.DoesNotContain("登録コード:", revoked.Output);
        Assert.DoesNotContain("Now listening", revoked.Output);
        Assert.Equal(codeCount, app.Database.Scalar("SELECT COUNT(*) FROM auth_enrollment_codes"));
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        var issued = await RunAsync(app.Database.ConnectionString, ["issue-recovery-link", "--account-id", app.UserId]);
        Assert.Equal(0, issued.ExitCode);
        Assert.Equal(1, issued.Output.Split('\n').Count(line => line.StartsWith(LoginTestApp.Origin + "/auth/enroll#token=", StringComparison.Ordinal)));
        Assert.DoesNotContain("Now listening", issued.Output);
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
        var url = issued.StandardOutput.Trim();
        Assert.StartsWith(LoginTestApp.Origin + "/auth/enroll#token=", url);
        var token = url.EnrollmentToken();
        Assert.Equal(32, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token).Length);
        Assert.DoesNotContain(token, issued.StandardError);
        Assert.Single(issued.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<Monetail.Api.Persistence.ApiDbContext>();
            var link = await database.EnrollmentCodes.AsNoTracking().SingleAsync(row => row.Purpose == EnrollmentCode.RecoveryPurpose);
            Assert.Equal(TimeSpan.FromMinutes(10), link.ExpiresAtUtc - link.CreatedAtUtc);
        }
        using var recovered = app.Client();
        await LoginTestApp.CsrfAsync(recovered);
        using var optionsResponse = await recovered.PostAsJsonAsync("/auth/enrollment/options", new { token, displayName = "改名を要求" });
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var key = RandomNumberGenerator.GetBytes(32);
        using var registered = await recovered.PostAsJsonAsync("/auth/enrollment/verify", new { credential = app.Attestation(options, key) });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal("同じ名前", app.Database.Scalar("SELECT DisplayName FROM AspNetUsers"));
        using var notSignedIn = await recovered.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, notSignedIn.StatusCode);
        options = await app.BeginAsync(recovered);
        using var signedIn = await app.VerifyAsync(recovered, app.AssertionFor(options, app.UserId, key));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task RestoreCommandResetsAllAccountsWithoutCodeOrHistoryChanges()
    {
        await using var app = await LoginTestApp.CreateAsync();
        await app.AddAccountAsync();
        var tables = app.Database.Tables().Where(table => table is not
            ("AspNetUsers" or "AspNetUserPasskeys" or "auth_sessions" or "auth_enrollment_codes")).ToArray();
        var before = app.Database.Snapshot(tables);
        var result = await RunAsync(app.Database.ConnectionString, ["reset-authentication", "--after-restore"]);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("登録コード:", result.Output);
        Assert.DoesNotContain("Now listening", result.Output);
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE IsEnabled<>0"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(before, app.Database.Snapshot(tables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDatabaseFailsWithoutCreatingFile(bool all)
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        var result = await RunAsync(fixture.ConnectionString, all
            ? ["reset-authentication", "--after-restore"]
            : ["revoke-account", "--account-id", "1", "--emergency-recovery"]);
        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(fixture.DatabasePath));
        Assert.DoesNotContain("登録コード:", result.Output);
        Assert.DoesNotContain("Now listening", result.Output);
    }

    [Theory]
    [InlineData(false, "Data Source=:memory:")]
    [InlineData(true, "Data Source=:memory:")]
    [InlineData(false, "unsupported-key=synthetic-secret")]
    [InlineData(true, "unsupported-key=synthetic-secret")]
    [InlineData(false, "synthetic-secret")]
    [InlineData(true, "synthetic-secret")]
    public async Task InvalidDatabaseConfigurationFailsCleanlyWithoutStartingWeb(bool restore, string connectionString)
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        var result = await RunAsync(connectionString, restore
            ? ["reset-authentication", "--after-restore"] : ["initialize-db"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("接続設定・構成ファイルを確認", result.Output);
        Assert.DoesNotContain("Unhandled exception", result.Output);
        Assert.DoesNotContain("synthetic-secret", result.Output);
        Assert.DoesNotContain("登録コード:", result.Output);
        Assert.DoesNotContain("Now listening", result.Output);
        Assert.False(File.Exists(fixture.DatabasePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://monetail.example.test")]
    [InlineData("https://monetail.example.test/path")]
    [InlineData("https://monetail.example.test#synthetic-secret")]
    public async Task LinkCommandsRequireFixedOriginAndNeverFallBackToCodes(string? origin)
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        var before = fixture.Snapshot(fixture.Tables());
        foreach (var args in new[] { new[] { "initialize-admin" }, ["issue-recovery-link", "--account-id", "unknown"] })
        {
            var result = await RunAsync(fixture.ConnectionString, args, origin);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.StandardOutput);
            Assert.DoesNotContain("synthetic-secret", result.Output);
            Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryCommandRejectsUnregisteredAccountsWithoutChangingInvitation(bool initialAdmin)
    {
        using var fixture = new AuthDatabaseFixture();
        await fixture.MigrateAsync();
        await using var services = fixture.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
        var invitation = (await provisioning.InitializeAdminAsync())!;
        if (!initialAdmin)
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AuthUser>>();
            var user = (await users.FindByIdAsync(invitation.AuthUserId))!;
            Assert.True((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded);
            invitation = await provisioning.IssueEnrollmentInvitationAsync(user.Id);
        }
        var before = fixture.Snapshot(fixture.Tables());
        var result = await RunAsync(fixture.ConnectionString, ["issue-recovery-link", "--account-id", invitation.AuthUserId]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.DoesNotContain(invitation.Token(), result.Output);
        Assert.Equal(before, fixture.Snapshot(fixture.Tables()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveryCommandRejectsRegisteredAccountThatIsEnabledOrStillHasPasskeys(bool enabled)
    {
        await using var app = await LoginTestApp.CreateAsync();
        var target = await app.AddAccountAsync();
        app.Database.Execute(enabled
            ? $"DELETE FROM AspNetUserPasskeys WHERE UserId='{target.UserId}'"
            : $"UPDATE AspNetUsers SET IsEnabled=0 WHERE Id='{target.UserId}'");
        var before = app.Database.Snapshot(app.Database.Tables());
        var result = await RunAsync(app.Database.ConnectionString, ["issue-recovery-link", "--account-id", target.UserId]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    /// <summary>発行だけは固定Originを与え、他のWeb設定なしで管理コマンドを起動する。</summary>
    private static async Task<(int ExitCode, string Output, string StandardOutput, string StandardError)> RunAsync(string connectionString, string[] arguments, string? origin = LoginTestApp.Origin)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Monetail.Api.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "Monetail.Api.Tests.deps.json"), typeof(Program).Assembly.Location }.Concat(arguments))
            start.ArgumentList.Add(argument);
        start.Environment["ConnectionStrings__Monetail"] = connectionString;
        if (origin is not null) start.Environment["Passkeys__Origin"] = origin;
        else start.Environment.Remove("Passkeys__Origin");
        start.Environment["DesktopOAuth__SigningCertificatePath"] = "invalid-oauth-setting";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var stdout = await output;
        var stderr = await error;
        return (process.ExitCode, stdout + stderr, stdout, stderr);
    }
}
