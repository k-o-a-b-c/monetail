using System.Net;
using System.Net.Http.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Management;

namespace Monetail.Windows.Presentation.Tests;

internal sealed class ManagementTestContext : IDisposable
{
    private readonly HttpClient http;
    public ManagementContext Context { get; }
    public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
    public ManagementTestContext(Func<HttpRequestMessage, HttpResponseMessage> respond, string role = "Admin")
    {
        var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
        http = new(new Handler(request =>
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));
            return respond(request);
        }));
        var api = new MonetailApiClient(endpoint, http);
        Context = new(api, new(api, endpoint, new Store()), new(), new(Guid.NewGuid(), "家計簿", role, "r"),
            Guid.NewGuid(), endpoint.Value, ExecuteAsync);
    }
    private async Task ExecuteAsync(Func<Task> action, Action<string>? failed)
    {
        Context.IsBusy = true;
        try { await action(); }
        catch (ApiConnectionException) { failed?.Invoke("接続できませんでした。"); }
        finally { Context.IsBusy = false; }
    }
    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    public void Dispose() { Context.Dispose(); http.Dispose(); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private sealed class Store : ITokenStore
    {
        private TokenSet? current = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        public TokenSet? Load(ApiEndpoint endpoint) => current;
        public void Save(ApiEndpoint endpoint, TokenSet tokens) => current = tokens;
        public void Clear() => current = null;
    }
}
