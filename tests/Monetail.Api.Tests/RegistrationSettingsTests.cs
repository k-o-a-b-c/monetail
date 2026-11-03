using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>登録モードの既定値・入力検証・通常API起動時の構成固定を検証する。</summary>
public sealed class RegistrationSettingsTests
{
    [Fact]
    public void MissingSettingDefaultsToInvitationOnly()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.Equal(RegistrationMode.InvitationOnly, RegistrationSettings.Read(config).Mode);
    }

    [Theory]
    [InlineData("InvitationOnly", RegistrationMode.InvitationOnly)]
    [InlineData("Public", RegistrationMode.Public)]
    public void ExplicitModesAreRecognized(string value, RegistrationMode expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Registration:Mode"] = value }).Build();
        Assert.Equal(expected, RegistrationSettings.Read(config).Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("public")]
    [InlineData("Public ")]
    [InlineData("Unrestricted")]
    [InlineData("0")]
    public void InvalidModesAreRejected(string value)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Registration:Mode"] = value }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => RegistrationSettings.Read(config));
        Assert.Contains("Registration:Mode", error.Message);
    }

    [Fact]
    public async Task StartupWithoutModeUsesInvitationOnlyAndDoesNotInitializeDatabase()
    {
        using var database = new AuthDatabaseFixture(createFile: false);
        await using var factory = NewFactory(database);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://monetail.example.test") });
        using var response = await client.GetAsync("/auth/enroll");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RegistrationMode.InvitationOnly,
            factory.Services.GetRequiredService<RegistrationSettings>().Mode);
        Assert.False(File.Exists(database.DatabasePath));
    }

    [Theory]
    [InlineData("InvitationOnly", RegistrationMode.InvitationOnly)]
    [InlineData("Public", RegistrationMode.Public)]
    public async Task StartupRegistersExplicitModeWithoutChangingExistingEnrollment(string value, RegistrationMode expected)
    {
        using var database = new AuthDatabaseFixture(createFile: false);
        await using var factory = NewFactory(database, value);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://monetail.example.test") });
        using var response = await client.GetAsync("/auth/enroll");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, factory.Services.GetRequiredService<RegistrationSettings>().Mode);
        Assert.False(File.Exists(database.DatabasePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Public ")]
    public async Task InvalidModeStopsApiBeforeOpeningDatabase(string value)
    {
        using var database = new AuthDatabaseFixture(createFile: false);
        await using var factory = NewFactory(database, value);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Registration:Mode", error.Message);
        Assert.False(File.Exists(database.DatabasePath));
    }

    private static WebApplicationFactory<Program> NewFactory(AuthDatabaseFixture database, string? mode = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseApiTestSettings(database);
            if (mode is not null) builder.UseSetting("Registration:Mode", mode);
        });
}
