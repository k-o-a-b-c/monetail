using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;

namespace Monetail.Api.Tests;

/// <summary>実Identityと隔離SQLiteを使い、自己登録の時刻・送信元と再起動を制御する。</summary>
internal sealed class SelfRegistrationTestApp : IAsyncDisposable
{
    public AuthDatabaseFixture Database { get; } = new();
    public RegistrationTime Time { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public const string Root = "/auth/self-registration";

    public static async Task<SelfRegistrationTestApp> CreateAsync(string mode = "Public", Action<IWebHostBuilder>? configure = null)
    {
        var app = new SelfRegistrationTestApp();
        await app.Database.MigrateAsync();
        app.Factory = app.NewFactory(mode, configure);
        return app;
    }

    public WebApplicationFactory<Program> NewFactory(string mode = "Public", Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseApiTestSettings(Database).UseSetting("Registration:Mode", mode);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(Time);
                services.AddSingleton<IStartupFilter, TestAddress>();
            });
            configure?.Invoke(builder);
        });

    public async Task RestartAsync(string mode = "Public")
    {
        await Factory.DisposeAsync();
        Factory = NewFactory(mode);
    }

    public HttpClient Client(string ip = "192.0.2.1", WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? Factory).CreateClient(new() { BaseAddress = new(LoginTestApp.Origin), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", LoginTestApp.Origin);
        client.DefaultRequestHeaders.Add("X-Test-IP", ip);
        return client;
    }

    public static async Task<JsonElement> BeginAsync(HttpClient client, string name = "同じ名前")
    {
        await LoginTestApp.CsrfAsync(client);
        using var response = await client.PostAsJsonAsync(Root + "/options", new { displayName = name });
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static Task<HttpResponseMessage> VerifyAsync(HttpClient client, object credential) =>
        client.PostAsJsonAsync(Root + "/verify", new { credential });

    public async Task SeedCompletionsAsync(int count, DateTimeOffset at)
    {
        using var connection = Database.OpenForeignKeyConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO auth_self_registration_completions(CompletedAtUtc) VALUES($at)";
            command.Parameters.AddWithValue("$at", at.UtcDateTime);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        Database.Dispose();
    }

    internal sealed class RegistrationTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }

    // テスト専用ヘッダーを本当の接続元へ反映し、本番へ入力の抜け道を追加しない。
    private sealed class TestAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, continuation) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(http.Request.Headers["X-Test-IP"].FirstOrDefault() ?? "192.0.2.1");
                return continuation(http);
            });
            next(app);
        };
    }
}
