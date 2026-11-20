using Monetail.Windows.Client;
using Monetail.Windows.Presentation.Connection;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ConnectionSettingsViewModelTests
{
    private static readonly ApiEndpoint Endpoint = ApiEndpoint.Parse("https://api.example.test/");
    [Fact]
    public void InitialConstructionDoesNotConnectAndKeepsOnlyTheInput()
    {
        var calls = 0;
        using var model = new ConnectionSettingsViewModel(null, (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });
        Assert.Equal("", model.ServerUrl);
        Assert.Null(model.UrlError);
        Assert.False(model.CanCancel);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.False(model.IsConnecting);
        Assert.True(model.ConnectCommand.CanExecute(null));
        Assert.Equal(0, calls);
    }
    [Theory]
    [InlineData("")]
    [InlineData("http://invalid.example.test/")]
    [InlineData("https://example.test/path")]
    [InlineData("https://user:pass@example.test/")]
    [InlineData("https://example.test/?query")]
    public async Task InvalidUrlUsesClientValidationWithoutChangingTheInput(string url)
    {
        var calls = 0;
        using var model = new ConnectionSettingsViewModel(null, (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        })
        {
            ServerUrl = url
        };
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(url, model.ServerUrl);
        Assert.Equal(Assert.Throws<FormatException>(() => ApiEndpoint.Parse(url)).Message, model.UrlError);
        Assert.Equal(0, calls);
        model.ServerUrl = Endpoint.Value;
        Assert.Null(model.UrlError);
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task ConnectionPassesParsedEndpointAndKeepsTheOriginalTextDuringExecution()
    {
        var entered = new TaskCompletionSource<ApiEndpoint>();
        var finish = new TaskCompletionSource();
        using var model = new ConnectionSettingsViewModel(Endpoint, async (candidate, _) =>
        {
            entered.SetResult(candidate);
            await finish.Task;
        }, () => Task.CompletedTask);
        model.ServerUrl = "  https://OTHER.example.test  ";
        var states = new List<string?>();
        model.PropertyChanged += (_, args) => states.Add(args.PropertyName);
        var running = model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("https://other.example.test/", (await entered.Task).Value);
        Assert.True(model.IsConnecting);
        Assert.False(model.ConnectCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.Equal("  https://OTHER.example.test  ", model.ServerUrl);
        finish.SetResult();
        await running;
        Assert.False(model.IsConnecting);
        Assert.True(model.CancelCommand.CanExecute(null));
        Assert.Contains(nameof(model.IsConnecting), states);
    }
    [Fact]
    public async Task FailedOperationDoesNotRetryOrReplaceInputAndCanBeRetriedExplicitly()
    {
        var calls = 0;
        using var model = new ConnectionSettingsViewModel(Endpoint, (_, _) => ++calls == 1 ? Task.FromException(new HttpRequestException()) : Task.CompletedTask);
        await Assert.ThrowsAsync<HttpRequestException>(() => model.ConnectCommand.ExecuteAsync(null));
        Assert.Equal(1, calls);
        Assert.Equal(Endpoint.Value, model.ServerUrl);
        Assert.False(model.IsConnecting);
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task CancelOnlyInvokesTheSuppliedNavigationAndDoesNotConnect()
    {
        var calls = 0;
        using var model = new ConnectionSettingsViewModel(Endpoint, (_, _) => throw new InvalidOperationException(), () =>
        {
            calls++;
            return Task.CompletedTask;
        });
        Assert.True(model.CanCancel);
        await model.CancelCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        Assert.Equal(Endpoint.Value, model.ServerUrl);
    }
    [Fact]
    public async Task ReleasingTheScreenCancelsTheCurrentReadAndPreventsFurtherOperations()
    {
        var requests = 0;
        CancellationToken token = default;
        var entered = new TaskCompletionSource();
        var model = new ConnectionSettingsViewModel(Endpoint, async (_, ct) =>
        {
            requests++;
            token = ct;
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var operation = model.ConnectCommand.ExecuteAsync(null);
        await entered.Task;
        model.Dispose();
        model.Dispose();
        await operation;
        Assert.True(token.IsCancellationRequested);
        Assert.False(model.IsConnecting);
        Assert.False(model.ConnectCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(1, requests);
    }
}
