using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class InitialMasterValidationTests
{
    [Theory]
    [InlineData("tax-Standard")] [InlineData("tax-ReducedFood")] [InlineData("tax-ReducedNewspaper")] [InlineData("tax-Exempt")]
    [InlineData("missingTax")] [InlineData("missingClient")] [InlineData("clientId")] [InlineData("clientType")]
    [InlineData("applicationType")] [InlineData("consentType")] [InlineData("clientSecret")] [InlineData("jwks")] [InlineData("settings")]
    [InlineData("redirectChanged")] [InlineData("redirectExtra")] [InlineData("redirectMissing")] [InlineData("logoutExtra")]
    [InlineData("pkceMissing")] [InlineData("requirementChanged")] [InlineData("permissionsMissing")]
    [InlineData("permissionMissing")] [InlineData("permissionChanged")] [InlineData("permissionExtra")]
    [InlineData("invalidJson")] [InlineData("jsonObject")] [InlineData("jsonNull")] [InlineData("nonStringItem")] [InlineData("blob")]
    public async Task FixedMasterMismatchRejectsInitializationAndHttpWithoutWritingOrLeakingValues(string change)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = app.Client(); await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        using var created = await client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "保持する家計簿" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        app.Database.Execute(Change(app, change));
        var before = app.Database.Snapshot(app.Database.Tables());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Database.InitializeAsync());
        Assert.Contains(change.StartsWith("tax-", StringComparison.Ordinal) || change == "missingTax" ? "税区分" : "OAuth", error.Message);
        Assert.Contains("DBは変更していません", error.Message); Assert.DoesNotContain("synthetic-secret", error.Message);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        foreach (var path in new[] { "/auth/me", "/ledgers" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(app.UserId, body); Assert.DoesNotContain("synthetic-secret", body);
        }
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("reverse")] [InlineData("whitespace")] [InlineData("duplicates")]
    public async Task JsonArraysAreComparedAsSetsAndAreNotCanonicalizedInStorage(string format)
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await app.LoginAsync(client);
        foreach (var field in new[] { "Permissions", "Requirements", "RedirectUris", "PostLogoutRedirectUris" })
        {
            var stored = app.Database.Scalar($"SELECT {field} FROM OpenIddictApplications") as string;
            var values = stored is null ? [] : JsonSerializer.Deserialize<string[]>(stored)!;
            var reordered = format == "duplicates" ? values.Reverse().Concat(values).ToArray() : values.Reverse().ToArray();
            var json = JsonSerializer.Serialize(reordered, new JsonSerializerOptions { WriteIndented = format == "whitespace" });
            app.Database.Execute(Set(field, json));
        }
        var before = app.Database.Snapshot(app.Database.Tables());
        await app.Database.InitializeAsync(); await app.Database.InitializeAsync();
        using var me = await client.GetAsync("/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task OperationalValuesCanChangeAndRevalidationPreservesAccountsPasskeysLedgersAndMetadata()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client();
        await app.LoginAsync(client); await LoginTestApp.CsrfAsync(client);
        using var ledger = await client.PostAsJsonAsync("/ledgers", new { requestId = Guid.NewGuid(), name = "運用中" });
        Assert.Equal(HttpStatusCode.Created, ledger.StatusCode);
        app.Database.Execute("""
            UPDATE OpenIddictApplications SET ConcurrencyToken='operational-revision',DisplayName='運用表示名',
                DisplayNames='{"ja":"任意の表示"}',Properties='{"operational":"metadata"}';
            """);
        var before = app.Database.Snapshot(app.Database.Tables());
        await app.Database.InitializeAsync();
        using var me = await client.GetAsync("/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var ledgers = await client.GetAsync("/ledgers"); Assert.Equal(HttpStatusCode.OK, ledgers.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    private static string Change(LoginTestApp app, string change)
    {
        if (change.StartsWith("tax-", StringComparison.Ordinal)) return $"UPDATE tax_categories SET name='改変名' WHERE code='{change[4..]}'";
        var permissions = JsonSerializer.Deserialize<string[]>((string)app.Database.Scalar("SELECT Permissions FROM OpenIddictApplications")!)!;
        return change switch
        {
            "missingTax" => "DELETE FROM tax_categories WHERE code='Standard'",
            "missingClient" => "DELETE FROM OpenIddictApplications",
            "clientId" => Set("ClientId", "changed-client"), "clientType" => Set("ClientType", "confidential"),
            "applicationType" => Set("ApplicationType", "web"), "consentType" => Set("ConsentType", "explicit"),
            "clientSecret" => Set("ClientSecret", "synthetic-secret"), "jwks" => Set("JsonWebKeySet", "{}"),
            "settings" => Set("Settings", "{\"tkn_lft:act\":\"30.00:00:00\"}"),
            "redirectChanged" => Set("RedirectUris", "[\"https://changed.example/callback\"]"),
            "redirectExtra" => Set("RedirectUris", "[\"http://127.0.0.1/callback/\",\"https://changed.example/callback\"]"),
            "redirectMissing" => Set("RedirectUris", null), "logoutExtra" => Set("PostLogoutRedirectUris", "[\"https://changed.example/logout\"]"),
            "pkceMissing" => Set("Requirements", "[]"), "requirementChanged" => Set("Requirements", "[\"ft:par\"]"),
            "permissionsMissing" => Set("Permissions", null), "permissionMissing" => Set("Permissions", JsonSerializer.Serialize(permissions.Skip(1))),
            "permissionChanged" => Set("Permissions", JsonSerializer.Serialize(permissions.Skip(1).Append("ept:revocation"))),
            "permissionExtra" => Set("Permissions", JsonSerializer.Serialize(permissions.Append("gt:client_credentials"))),
            "invalidJson" => Set("Permissions", "["), "jsonObject" => Set("Permissions", "{}"), "jsonNull" => Set("Permissions", "null"),
            "nonStringItem" => Set("Requirements", "[\"ft:pkce\",1]"),
            _ => "UPDATE OpenIddictApplications SET ClientSecret=X'00FF'"
        };
    }
    private static string Set(string field, string? value) => $"UPDATE OpenIddictApplications SET {field}=" +
        (value is null ? "NULL" : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'");
}
