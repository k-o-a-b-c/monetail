using System.Diagnostics;
using System.Net;
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
        var id = first.Output.Split('\n').Single(line => line.StartsWith("アカウントID:")).Split(':', 2)[1].Trim();
        var repeated = await RunAsync(fixture.ConnectionString, ["initialize-admin"]);
        Assert.Equal(0, repeated.ExitCode);
        Assert.Contains(id, repeated.Output);
        Assert.DoesNotContain("登録コード:", repeated.Output);
        var initializedAgain = await RunAsync(fixture.ConnectionString, ["initialize-db"]);
        Assert.Equal(0, initializedAgain.ExitCode);
    }

    [Fact]
    public async Task UnknownCommandFailsWithoutStartingWebOrCreatingDatabase()
    {
        using var fixture = new AuthDatabaseFixture(createFile: false);
        var result = await RunAsync(fixture.ConnectionString, ["unknown-command"]);
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
        Assert.Contains("アカウントID:", created.Output);
        Assert.Equal(1, created.Output.Split('\n').Count(line => line.StartsWith("登録コード:", StringComparison.Ordinal)));
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
        var issued = await RunAsync(app.Database.ConnectionString, ["issue-enrollment-code", "--account-id", app.UserId]);
        Assert.Equal(0, issued.ExitCode);
        Assert.Equal(1, issued.Output.Split('\n').Count(line => line.StartsWith("登録コード:", StringComparison.Ordinal)));
        Assert.DoesNotContain("Now listening", issued.Output);
        Assert.Equal(0L, app.Database.Scalar("SELECT IsEnabled FROM AspNetUsers"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
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

    /// <summary>Web設定が不正でも、実際のAPIエントリーポイントを管理コマンドとして起動する。</summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(string connectionString, string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Monetail.Api.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "Monetail.Api.Tests.deps.json"), typeof(Program).Assembly.Location }.Concat(arguments))
            start.ArgumentList.Add(argument);
        start.Environment["ConnectionStrings__Monetail"] = connectionString;
        start.Environment["Passkeys__Origin"] = "invalid-web-setting";
        start.Environment["DesktopOAuth__SigningCertificatePath"] = "invalid-oauth-setting";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
