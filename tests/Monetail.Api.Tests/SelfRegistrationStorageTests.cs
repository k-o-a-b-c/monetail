using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>既存APIの鍵を保持する明示移行と、アカウントから独立した成功記録を確認する。</summary>
public sealed class SelfRegistrationStorageTests
{
    [Fact]
    public async Task InitializationCreatesIndependentSuccessBudgetAndKeepsItOnReexecution()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        db.Execute("INSERT INTO auth_self_registration_completions(CompletedAtUtc) VALUES('2026-11-01')");
        var before = db.Snapshot(db.Tables()); await db.InitializeAsync();
        Assert.Equal(before,db.Snapshot(db.Tables()));
        Assert.Equal(0L,db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_list('auth_self_registration_completions')"));
    }

    [Fact]
    public async Task MissingStorageFailsClosedAndNeverCreatesTheTableOnHttpRequests()
    {
        await using var app = await SelfRegistrationTestApp.CreateAsync();
        app.Database.Execute("DROP TABLE auth_self_registration_completions");
        using var client = app.Client();
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync(SelfRegistrationTestApp.Root + "/options", new { displayName = "name" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("auth_self_registration_completions", app.Database.Tables());
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
    }

    [Fact]
    public async Task DeletingAnAccountDoesNotEraseTheSuccessBudget()
    {
        await using var app = await SelfRegistrationTestApp.CreateAsync();
        using var client = app.Client();
        var options = await SelfRegistrationTestApp.BeginAsync(client);
        using var registered = await SelfRegistrationTestApp.VerifyAsync(client, PasskeyEnrollmentTests.Credential(options));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var connection = app.Database.OpenForeignKeyConnection();
        AuthDatabaseFixture.Execute(connection, "DELETE FROM AspNetUsers");
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM AspNetUserPasskeys"));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM auth_self_registration_completions"));
    }
}
