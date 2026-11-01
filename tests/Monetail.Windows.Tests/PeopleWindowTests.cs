using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Tests;

public sealed class PeopleWindowTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Editor")]
    [InlineData("Viewer")]
    public Task ListShowsEveryoneSelfFirstAndOnlyPermittedButtons(string role) => OnUiThread(() =>
    {
        using var f = new Fixture(role);
        f.Open();
        var list = f.List;
        Assert.Equal(new[] { "本人（本人）", "家族" }, list.Items.Cast<ListViewItem>().Select(row => row.Text));
        Assert.False(f.Button("編集").Enabled);
        Assert.Equal(role == "Admin", f.Button("追加").Enabled);
        Assert.Equal(role == "Admin", f.Button("並び順を変更").Enabled);
        f.Select(1);
        Assert.Equal(role == "Admin", f.Button("編集").Enabled);
        f.Select(0);
        Assert.True(f.Button("編集").Enabled);
        f.Click("編集");
        Assert.Equal(role != "Admin", f.Input("有効開始日").ReadOnly);
        Assert.Empty(f.Window.OwnedForms);
        Assert.DoesNotContain(All(f.Window), control => control is ListView);
        Assert.Single(f.Api.Calls, row => row.Method == HttpMethod.Get && row.Path.EndsWith("/people"));
    });

    [Fact]
    public Task CreationValidatesInPlaceAndRetryKeepsRequestThenReturnsResponseList() => OnUiThread(() =>
    {
        using var f = new Fixture(); f.Open(); f.Click("追加");
        Assert.All(All(f.Window).OfType<TextBox>(), input => Assert.Equal("", input.Text));
        var before = f.Api.Calls.Count;
        f.Input("人物名").Text = new string('あ', 101);
        f.Click("登録");
        Assert.Contains(All(f.Window), control => control.Visible && control.Text == "人物名は100文字以内で入力してください。");
        Assert.Equal(before, f.Api.Calls.Count);
        Assert.Equal(101, f.Input("人物名").Text.Length);
        f.Input("人物名").Text = "  新しい人物  ";
        f.Input("有効開始日").Text = "2026/10/1";
        f.Input("有効終了日").Text = "2026/9/30";
        f.Click("登録");
        Assert.Equal(before, f.Api.Calls.Count);
        f.Input("有効終了日").Text = "";
        f.Api.FailCreateOnce = true;
        f.Click("登録");
        Assert.Equal("  新しい人物  ", f.Input("人物名").Text);
        f.Click("登録");
        Assert.Equal(3, f.List.Items.Count);
        Assert.Equal(f.Api.Creates[0], f.Api.Creates[1]);
        Assert.Equal(before + 2, f.Api.Calls.Count);
    });

    [Fact]
    public Task ConflictKeepsDraftShowsEveryLatestFieldAndRequiresExplicitSwitch() => OnUiThread(() =>
    {
        using var f = new Fixture(); f.Open(); f.Select(0); f.Click("編集");
        f.Input("人物名").Text = "未保存の入力";
        f.Api.ConflictEditOnce = true;
        var before = f.Api.Calls.Count;
        f.Click("保存");
        Assert.Equal("未保存の入力", f.Input("人物名").Text);
        Assert.False(f.Button("保存").Enabled);
        Assert.Contains(All(f.Window), control => control.Visible && control.Text == "人物名：最新の本人");
        Assert.Contains(All(f.Window), control => control.Visible && control.Text == "有効開始日：2026/04/01");
        Assert.Contains(All(f.Window), control => control.Visible && control.Text == "有効終了日：2026/09/30");
        Assert.Equal(before + 1, f.Api.Calls.Count);
        f.Click("最新内容に切り替える");
        Assert.Equal("最新の本人", f.Input("人物名").Text);
        Assert.True(f.Button("保存").Enabled);
        f.Click("保存");
        Assert.Equal("最新の本人（本人）", f.List.Items[0].Text);
        Assert.Equal(f.Api.People[1].Revision, f.Api.LastEditRevision);
        Assert.Equal(before + 2, f.Api.Calls.Count);
    });

    [Fact]
    public Task NameOnlyViewerEditOmitsPeriodsAndReturnsToListWithoutGet() => OnUiThread(() =>
    {
        using var f = new Fixture("Viewer"); f.Open(); f.Select(0); f.Click("編集");
        f.Input("人物名").Text = "改名";
        var before = f.Api.Calls.Count;
        f.Click("保存");
        Assert.Equal("改名（本人）", f.List.Items[0].Text);
        Assert.False(f.Api.LastEditHasPeriod);
        Assert.Equal(before + 1, f.Api.Calls.Count);
    });

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public Task LosingAuthorityKeepsDraftWithoutRefetchAndCannotReopen(HttpStatusCode status) => OnUiThread(() =>
    {
        using var f = new Fixture(); f.Open(); f.Select(0); f.Click("編集");
        f.Input("人物名").Text = "保持する入力";
        f.Api.Rejection = status;
        var before = f.Api.Calls.Count;
        f.Click("保存");
        Assert.Equal("保持する入力", f.Input("人物名").Text);
        Assert.Equal(before + 1, f.Api.Calls.Count);
        f.Click("キャンセル");
        Assert.DoesNotContain(All(f.Window), control => control is ListView);
        Assert.Contains(All(f.Window).OfType<Button>(), button => button.Text == "ホームへ戻る");
    });

    [Fact]
    public Task OrderingUsesCommonOrderMovesLocallyAndReplacesConflictInOneResponse() => OnUiThread(() =>
    {
        using var f = new Fixture(); f.Open(); f.Click("並び順を変更");
        Assert.Equal("家族", f.List.Items[0].Text);
        var before = f.Api.Calls.Count;
        f.Select(1); f.Click("上へ");
        Assert.Equal("本人", f.List.Items[0].Text);
        Assert.Equal(before, f.Api.Calls.Count);
        f.Api.ConflictOrderOnce = true;
        f.Click("保存");
        Assert.Equal("家族", f.List.Items[0].Text);
        Assert.Contains(All(f.Window), control => control.Visible && control.Text.StartsWith("ほかの操作で変更されています。最新の並び順"));
        Assert.Equal(before + 1, f.Api.Calls.Count);
        f.Select(1); f.Click("上へ"); f.Click("保存");
        Assert.Equal(f.Api.OrderRevision, f.Api.LastOrderRevision);
        Assert.Equal(before + 2, f.Api.Calls.Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeletionConfirmsNameAndRejectsReferencesOrUsesReturnedList(bool referenced) => OnUiThread(() =>
    {
        using var f = new Fixture(); f.Open(); f.Select(1);
        string? confirmedName = null;
        f.Window.ConfirmPersonDeletion = name => { confirmedName = name; return false; };
        var before = f.Api.Calls.Count;
        f.Click("削除"); Assert.Equal(before, f.Api.Calls.Count);
        f.Window.ConfirmPersonDeletion = name => { confirmedName = name; return true; };
        f.Api.Referenced = referenced;
        f.Click("削除");
        Assert.Equal("家族", confirmedName);
        Assert.Equal(referenced ? 2 : 1, f.List.Items.Count);
        if (referenced) Assert.Contains(All(f.Window), control => control.Visible && control.Text.Contains("参照されているため削除できません"));
        Assert.Equal(before + 1, f.Api.Calls.Count);
    });

    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(All(control)));
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(ready(), "画面操作が完了しませんでした。");
    }
    private static Task OnUiThread(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-people-ui", Guid.NewGuid().ToString("N"));
        public ApiStub Api { get; }
        public MainWindow Window { get; }
        public ListView List => Assert.Single(All(Window).OfType<ListView>());
        public Button Button(string name) => Assert.Single(All(Window).OfType<Button>(), button => button.Text == name);
        public TextBox Input(string name) => Assert.Single(All(Window).OfType<TextBox>(), input => input.AccessibleName == name);
        public Fixture(string role = "Admin")
        {
            Api = new(role);
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            new ClientSettingsStore(directory).Save(endpoint);
            new FileTokenStore(directory, new DpapiDataProtector()).Save(endpoint,
                new TokenSet("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10)));
            Window = new(directory, new HttpClient(Api)); _ = Window.Handle;
            Complete(Window.InitializeAsync()); Window.Show();
        }
        public void Open()
        {
            var root = Assert.Single(Window.MainMenuStrip!.Items.OfType<ToolStripMenuItem>());
            Assert.Single(root.DropDownItems.OfType<ToolStripMenuItem>(), item => item.Text == "人物管理").PerformClick();
            PumpUntil(() => All(Window).OfType<ListView>().Any());
        }
        public void Click(string name)
        {
            Button(name).PerformClick();
            var root = Window.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single();
            PumpUntil(() => root.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "接続先の設定").Enabled);
        }
        public void Select(int index)
        {
            foreach (ListViewItem row in List.Items) row.Selected = false;
            List.Items[index].Selected = true; List.Items[index].Focused = true; Application.DoEvents();
        }
        public void Dispose() { Window.Close(); Window.Dispose(); Directory.Delete(directory, true); }
    }

    private sealed class ApiStub(string role) : HttpMessageHandler
    {
        public List<PersonInfo> People { get; } = [new(Guid.NewGuid(), "家族", null, null, 0, Guid.NewGuid()),
            new(Guid.NewGuid(), "本人", null, null, 1, Guid.NewGuid())];
        public Guid OrderRevision { get; private set; } = Guid.NewGuid();
        private readonly Guid ledgerId = Guid.NewGuid();
        public List<(HttpMethod Method, string Path)> Calls { get; } = [];
        public List<string> Creates { get; } = [];
        public bool FailCreateOnce, ConflictEditOnce, ConflictOrderOnce, Referenced, LastEditHasPeriod;
        public HttpStatusCode? Rejection;
        public Guid LastEditRevision, LastOrderRevision;
        private PeopleInfo Data() => new(People.OrderBy(row => row.DisplayOrder).ToArray(), People[1].PersonId, role, OrderRevision);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Calls.Add((request.Method, path));
            if (path == "/.well-known/openid-configuration") return Json(new { issuer = "https://synthetic-api.example/",
                authorization_endpoint = "https://synthetic-api.example/connect/authorize", token_endpoint = "https://synthetic-api.example/connect/token",
                response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { "S256" } });
            if (path == "/auth/me") return Json(new { authUserId = Guid.NewGuid(), displayName = "合成アカウント",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow });
            if (path == "/ledgers/open-last") return Json(new { ledger = new LedgerInfo(ledgerId, "家計簿", role, Guid.NewGuid().ToString("N")) });
            if (Rejection is { } status) return new(status);
            if (request.Method == HttpMethod.Get) return Json(Data());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
            if (request.Method == HttpMethod.Post)
            {
                Creates.Add(body.GetRawText());
                if (FailCreateOnce) { FailCreateOnce = false; return new(HttpStatusCode.ServiceUnavailable); }
                People.Add(new(Guid.NewGuid(), body.GetProperty("name").GetString()!, body.GetProperty("validFrom").GetDateTimeAsDateOnly(), null, 2, Guid.NewGuid()));
            }
            else if (request.Method == HttpMethod.Patch)
            {
                LastEditRevision = body.GetProperty("revision").GetGuid();
                LastEditHasPeriod = body.TryGetProperty("validFrom", out _);
                if (ConflictEditOnce)
                {
                    ConflictEditOnce = false;
                    People[1] = People[1] with { Name = "最新の本人", ValidFrom = new(2026, 4, 1), ValidTo = new(2026, 9, 30), Revision = Guid.NewGuid() };
                    return Json(new { code = "revision_conflict", latest = People[1] }, HttpStatusCode.Conflict);
                }
                People[1] = People[1] with { Name = body.GetProperty("name").GetString()! };
            }
            else if (request.Method == HttpMethod.Delete)
            {
                if (Referenced) return Json(new { code = "person_referenced" }, HttpStatusCode.Conflict);
                // 削除対象は本人以外。対応する本人を配列末尾として維持する。
                People.RemoveAt(0);
                return Json(new PeopleInfo(People.ToArray(), People[0].PersonId, role, Guid.NewGuid()));
            }
            else if (request.Method == HttpMethod.Put)
            {
                LastOrderRevision = body.GetProperty("revision").GetGuid();
                if (ConflictOrderOnce)
                {
                    ConflictOrderOnce = false; OrderRevision = Guid.NewGuid();
                    return Json(new { code = "revision_conflict", latest = Data() }, HttpStatusCode.Conflict);
                }
            }
            return Json(Data());
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }
}

internal static class PeopleDateTestExtensions
{
    internal static DateOnly? GetDateTimeAsDateOnly(this JsonElement value) => value.ValueKind == JsonValueKind.Null ? null :
        DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
