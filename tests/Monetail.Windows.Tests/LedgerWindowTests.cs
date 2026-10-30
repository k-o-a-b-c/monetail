using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Tests;

public sealed class LedgerWindowTests
{
    [Theory]
    [InlineData(0, "家計簿の作成", 0)]
    [InlineData(1, "レシート一覧", 1)]
    [InlineData(2, "家計簿の選択", 0)]
    public Task StartupShowsTheDestinationForCurrentMembership(int count, string expected, int opens) => OnUiThread(() =>
    {
        using var fixture = new Fixture(count);
        Complete(fixture.Window.InitializeAsync());
        Assert.Contains(Descendants(fixture.Window), control => control.Text == expected);
        Assert.Equal(opens, fixture.Api.OpenCount);
        Assert.DoesNotContain(Descendants(fixture.Window).OfType<Button>(), button => button.Text == "ログイン");
        if (count == 2) Assert.False(Assert.Single(Descendants(fixture.Window).OfType<Button>(), button => button.Text == "開く").Enabled);
    });

    [Fact]
    public Task SelectingTheSecondSameNamedLedgerOpensItsId() => OnUiThread(() =>
    {
        using var fixture = new Fixture(2);
        Complete(fixture.Window.InitializeAsync());
        fixture.Window.Show();
        var list = Assert.Single(Descendants(fixture.Window).OfType<ListBox>());
        list.SelectedIndex = 1;
        Click(fixture.Window, "開く");
        PumpUntil(() => Descendants(fixture.Window).Any(control => control.Text == "レシート一覧"));
        Assert.Equal(fixture.Api.Rows[1].LedgerId, fixture.Api.LastOpened);
    });

    [Fact]
    public Task RetryingCreationKeepsTheRequestIdAndOnlyOpensAfterSuccess() => OnUiThread(() =>
    {
        using var fixture = new Fixture(0, failCreationOnce: true);
        Complete(fixture.Window.InitializeAsync());
        fixture.Window.Show();
        Assert.Single(Descendants(fixture.Window).OfType<TextBox>()).Text = "新しい家計簿";
        Click(fixture.Window, "作成");
        PumpUntil(() => Descendants(fixture.Window).OfType<Button>().Any(button => button.Text == "再試行"));
        Assert.Equal(0, fixture.Api.OpenCount);
        Click(fixture.Window, "再試行");
        PumpUntil(() => Descendants(fixture.Window).Any(control => control.Text == "レシート一覧"));
        Assert.Equal(2, fixture.Api.Creations.Count);
        Assert.Equal(fixture.Api.Creations[0], fixture.Api.Creations[1]);
        Assert.Equal(1, fixture.Api.OpenCount);
    });

    private static void Click(Control root, string text) => Assert.Single(
        Descendants(root).OfType<Button>(), button => button.Text == text).PerformClick();

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> completed)
    {
        var timeout = Stopwatch.StartNew();
        while (!completed() && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(completed(), "画面操作が完了しませんでした。");
    }

    private static Task OnUiThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-ledger-ui", Guid.NewGuid().ToString("N"));
        public ApiStub Api { get; }
        public MainWindow Window { get; }
        public Fixture(int count, bool failCreationOnce = false)
        {
            Api = new ApiStub(count, failCreationOnce);
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            new ClientSettingsStore(directory).Save(endpoint);
            new FileTokenStore(directory, new DpapiDataProtector()).Save(endpoint,
                new TokenSet("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10)));
            Window = new MainWindow(directory, new HttpClient(Api));
            _ = Window.Handle;
        }
        public void Dispose()
        {
            Window.Close();
            Window.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ApiStub(int count, bool failCreationOnce) : HttpMessageHandler
    {
        public LedgerInfo[] Rows { get; } = Enumerable.Range(0, Math.Max(1, count))
            .Select(_ => new LedgerInfo(Guid.NewGuid(), "同じ家計簿名", "Admin", Guid.NewGuid().ToString("N"))).ToArray();
        public int OpenCount { get; private set; }
        public Guid? LastOpened { get; private set; }
        public List<Guid> Creations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/health") return new(HttpStatusCode.OK) { Content = new StringContent("Healthy") };
            if (path == "/.well-known/openid-configuration") return Json(new
            {
                issuer = "https://synthetic-api.example/", authorization_endpoint = "https://synthetic-api.example/connect/authorize",
                token_endpoint = "https://synthetic-api.example/connect/token", response_types_supported = new[] { "code" },
                code_challenge_methods_supported = new[] { "S256" }
            });
            if (path == "/auth/me") return Json(new { authUserId = Guid.NewGuid(), displayName = "合成アカウント",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow });
            if (path == "/ledgers/last-opened") return Json(new { ledger = (LedgerInfo?)null });
            if (path == "/ledgers" && request.Method == HttpMethod.Get) return Json(Rows.Take(count).ToArray());
            if (path == "/ledgers" && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
                Creations.Add(body.GetProperty("requestId").GetGuid());
                return failCreationOnce && Creations.Count == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(Rows[0]);
            }
            var selected = Rows.Single(row => path == $"/ledgers/{row.LedgerId:N}/open");
            Assert.Equal(HttpMethod.Post, request.Method);
            LastOpened = selected.LedgerId;
            OpenCount++;
            return Json(selected);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
