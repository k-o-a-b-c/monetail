using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckConnectionFlow(string output, bool capture)
    {
        using (var fixture = new SyntheticConnection())
        {
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Check(Visible(root, "ConnectionPage"), "未設定なら同じWindowで接続先を入力");
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            Check(Find<StackPanel>(root, "StartupNotice").Visibility == Visibility.Collapsed &&
                !Find<ProgressRing>(root, "StartupProgress").IsActive && Visible(root, "ConnectionPage"),
                "速い起動は待機表示を出さず、完了後に遅れて表示しない");
            if (capture) await Capture(root, output, "05-connection");
            Find<TextBox>(root, "ServerUrl").Text = "http://invalid.example.test/";
            Invoke(Find<Button>(root, "ConnectButton"));
            await Settle();
            Check(fixture.Requests.Count == 0 && Find<InfoBar>(root, "ErrorNotice").IsOpen &&
                Find<TextBox>(root, "ServerUrl").Text == "http://invalid.example.test/", "不正URLは送信せず入力を保持");

            Find<TextBox>(root, "ServerUrl").Text = SyntheticConnection.Endpoint.Value;
            Invoke(Find<Button>(root, "ConnectButton"));
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Requests.SequenceEqual(["/.well-known/openid-configuration"]) &&
                fixture.Settings.Load()!.Equals(SyntheticConnection.Endpoint), "メタデータ1回で接続を確認して設定を保存");
            if (capture) await Capture(root, output, "06-login");

            fixture.FailBrowser = true;
            Invoke(Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "BrowserPage") && Find<InfoBar>(root, "ErrorNotice").IsOpen);
            var waitingUri = fixture.AuthorizationUri;
            fixture.FailBrowser = false;
            Invoke(Find<Button>(root, "ReopenBrowserButton"));
            await Settle();
            Check(fixture.AuthorizationUri == waitingUri && fixture.BrowserOpens == 2,
                "ブラウザー起動失敗でも同じ認証要求で開き直せる");
            if (capture) await Capture(root, output, "07-browser-wait");
            Invoke(Find<Button>(root, "CancelLoginButton"));
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Tokens.Current is null, "ブラウザー認証のキャンセル後はログインで再試行可能");

            var previous = fixture.AuthorizationUri;
            fixture.CompleteLogin = true;
            Invoke(Find<Button>(root, "LoginButton"));
            await Wait(() => Visible(root, "LedgerCreatePage"));
            await fixture.CallbackTask!;
            Check(fixture.AuthorizationUri != previous && fixture.Tokens.Current is not null,
                "再試行は新しい認証要求で開始し、成功後に家計簿へ進む");
            Check(Find<TextBlock>(root, "NoLedgersNotice").Visibility == Visibility.Visible,
                "所属0件は家計簿作成と招待の案内");
            if (capture) await Capture(root, output, "08-ledger-create");

            Find<TextBox>(root, "NewLedgerName").Text = new string('a', 101);
            Invoke(Find<Button>(root, "CreateLedgerButton"));
            await Settle();
            Check(fixture.Creations.Count == 0 && Find<InfoBar>(root, "ErrorNotice").IsOpen, "家計簿名の上限超過を送信前に表示");
            Find<TextBox>(root, "NewLedgerName").Text = "  家族の家計簿  ";
            fixture.FailCreation = true;
            Invoke(Find<Button>(root, "CreateLedgerButton"));
            await Wait(() => Find<InfoBar>(root, "ErrorNotice").IsOpen && fixture.Creations.Count == 1);
            Check(Visible(root, "LedgerCreatePage") && Find<TextBox>(root, "NewLedgerName").Text == "  家族の家計簿  ",
                "作成の通信失敗でも名前と作成画面を保持");
            fixture.FailCreation = false;
            Invoke(Find<Button>(root, "CreateLedgerButton"));
            await Wait(() => Visible(root, "HomePage"));
            Check(fixture.Creations[0] == fixture.Creations[1] &&
                !fixture.Requests.Any(path => path.EndsWith("/open", StringComparison.Ordinal)),
                "作成再送は同じ要求IDを使い、作成応答から直接ホームを表示");
            Check(Find<TextBlock>(root, "LedgerName").Text == "家族の家計簿" &&
                !fixture.Requests.Any(path => path.EndsWith("/people", StringComparison.Ordinal)),
                "ホームはAPIの家計簿名を表示し、未表示の人物一覧を先読みしない");

            Invoke(Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 1);
            Check(Visible(root, "ManagementPage") && ((PersonRow)Find<ListView>(root, "PeopleList").Items[0]).Name == "父（本人）",
                "管理・設定の右側へAPIの人物一覧を表示");
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "ConnectionNavigation");
            await Settle();
            Check(Find<StackPanel>(root, "ConnectionSettingsContent").Visibility == Visibility.Visible &&
                navigation.IsPaneOpen, "接続先の設定でも左メニューを保持");
            navigation.SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
            await Settle();
            Check(Find<TextBlock>(root, "AccountName").Text == "テスト利用者", "ログイン情報は本人の表示名を表示");

            fixture.FailLogout = true;
            navigation.SelectedItem = Find<NavigationViewItem>(root, "LogoutNavigation");
            await Wait(() => Find<InfoBar>(root, "ErrorNotice").IsOpen);
            Check(fixture.Tokens.Current is not null && Visible(root, "ManagementPage"), "ログアウトの通信失敗では認証と画面を保持");
            fixture.FailLogout = false;
            navigation.SelectedItem = Find<NavigationViewItem>(root, "LogoutNavigation");
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Tokens.Current is null && fixture.Settings.Load() is not null,
                "ログアウト成功で認証だけを消し、接続先設定を保持");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.FailConnection = true;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Check(Visible(root, "FailurePage") && fixture.Tokens.Current is not null,
                "起動の通信失敗は認証を消さず再試行・接続先変更・終了を表示");
            fixture.FailConnection = false;
            fixture.HasPreviousLedger = true;
            Invoke(Find<Button>(root, "RetryButton"));
            await Wait(() => Visible(root, "HomePage"));
            Check(fixture.Requests.Count(path => path == "/ledgers/open-last") == 1 &&
                !fixture.Requests.Contains("/ledgers") && !fixture.Requests.Any(path => path.EndsWith("/open", StringComparison.Ordinal)),
                "保存ログインは本人確認後、open-lastの1応答から直接ホームへ復元");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.ChoiceCount = 2;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            var choices = Find<ListView>(root, "LedgerChoices");
            Check(Visible(root, "LedgerChoicePage") && choices.Items.Count == 2, "履歴なしで複数所属なら家計簿選択");
            choices.SelectedIndex = 1;
            Invoke(Find<Button>(root, "OpenLedgerButton"));
            await Wait(() => Visible(root, "HomePage"));
            Check(Find<TextBlock>(root, "LedgerName").Text == "もう一つの家計簿", "選択した家計簿を1回で開く");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.RejectAuthentication = true;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Check(Visible(root, "LoginPage") && fixture.Tokens.Current is null && fixture.Settings.Load() is not null,
                "失効した保存ログインは認証だけを破棄し接続設定を残す");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.HasPreviousLedger = true;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Invoke(Find<Button>(root, "SettingsButton"));
            await Wait(() => Find<ListView>(root, "PeopleList").Items.Count == 1);
            Find<NavigationView>(root, "ManagementNavigation").SelectedItem = Find<NavigationViewItem>(root, "ConnectionNavigation");
            await Settle();
            Find<TextBox>(root, "SettingsServerUrl").Text = "https://other.example.test/";
            Invoke(Find<Button>(root, "SettingsConnectButton"));
            await Wait(() => Visible(root, "LoginPage"));
            Check(fixture.Tokens.Current is null && fixture.Settings.Load()!.Value == "https://other.example.test/" &&
                fixture.OtherServerRequests.SequenceEqual(["/.well-known/openid-configuration"]),
                "接続先変更はメタデータ確認後に保存し、元の認証を変更先へ送らない");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.HasPreviousLedger = true;
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var root = await OpenSyntheticWindow(fixture);
            var startup = window!.StartAsync();
            Check(Visible(root, "StartupPage") && Find<StackPanel>(root, "StartupNotice").Visibility == Visibility.Collapsed &&
                !Find<ProgressRing>(root, "StartupProgress").IsActive, "接続待ちの最初の1秒は待機表示を出さない");
            await Wait(() => Find<StackPanel>(root, "StartupNotice").Visibility == Visibility.Visible);
            Check(Find<ProgressRing>(root, "StartupProgress").IsActive &&
                Find<ProgressRing>(root, "StartupProgress").IsEnabled && ReferenceEquals(window.Content, root),
                "1秒以上かかったら同じWindowでProgressRingと接続中を表示");
            if (capture) await Capture(root, output, "09-startup-wait");
            fixture.ConnectionGate.SetResult(true);
            await startup;
            Check(Visible(root, "HomePage") && !Find<ProgressRing>(root, "StartupProgress").IsActive &&
                Find<StackPanel>(root, "StartupNotice").Visibility == Visibility.Collapsed,
                "接続完了後は待機表示を止めて同じWindowのホームへ進む");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.ConnectionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Check(fixture.ConnectionCancelled && Visible(root, "FailurePage") && fixture.Tokens.Current is not null &&
                !Find<ProgressRing>(root, "StartupProgress").IsActive,
                "起動接続が10秒でタイムアウトしたら認証を残してエラー表示へ進む");
        }

        using (var fixture = new SyntheticConnection(saved: true))
        {
            fixture.HasPreviousLedger = true;
            fixture.RateLimitOpening = true;
            var root = await OpenSyntheticWindow(fixture);
            await window!.StartAsync();
            Check(Visible(root, "FailurePage") && Find<TextBlock>(root, "FailureMessage").Text.Contains("操作回数"),
                "起動先を開けない場合も待機画面に取り残さず再試行を表示");
            fixture.RateLimitOpening = false;
            Invoke(Find<Button>(root, "RetryButton"));
            await Wait(() => Visible(root, "HomePage"));
            Check(fixture.Tokens.Current is not null, "起動先の失敗から本人の再試行で復帰");
        }
    }

    private async Task<FrameworkElement> OpenSyntheticWindow(SyntheticConnection fixture)
    {
        var previous = window;
        window = new MainWindow(fixture.CreateConnection());
        var root = (FrameworkElement)window.Content;
        window.Activate();
        await Wait(() => root.IsLoaded);
        previous?.Close();
        return root;
    }

    private static bool Visible(FrameworkElement root, string name) => Find<Grid>(root, name).Visibility == Visibility.Visible;

    private sealed class MemoryTokens : ITokenStore
    {
        public TokenSet? Current { get; set; }
        public TokenSet? Load(ApiEndpoint endpoint) => Current;
        public void Save(ApiEndpoint endpoint, TokenSet tokens) => Current = tokens;
        public void Clear() => Current = null;
    }

    private sealed class SyntheticConnection : HttpMessageHandler
    {
        internal static readonly ApiEndpoint Endpoint = ApiEndpoint.Parse("https://api.example.test/");
        private readonly string directory = Path.Combine(Path.GetTempPath(), "monetail-winui-" + Guid.NewGuid().ToString("N"));
        private readonly LedgerInfo first = new(Guid.NewGuid(), "家族の家計簿", "Admin", Guid.NewGuid().ToString());
        private readonly LedgerInfo second = new(Guid.NewGuid(), "もう一つの家計簿", "Viewer", Guid.NewGuid().ToString());
        private readonly Guid person = Guid.NewGuid();
        internal readonly ClientSettingsStore Settings;
        internal readonly MemoryTokens Tokens = new();
        internal readonly List<string> Requests = [];
        internal readonly List<string> Creations = [];
        internal readonly List<string> OtherServerRequests = [];
        internal Uri? AuthorizationUri;
        internal Task? CallbackTask;
        internal TaskCompletionSource<bool>? ConnectionGate;
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? PeopleHandler;
        internal bool ConnectionCancelled, RateLimitOpening;
        internal bool CompleteLogin, FailCreation, FailLogout, FailConnection, HasPreviousLedger, FailBrowser, RejectAuthentication;
        internal int ChoiceCount, BrowserOpens;

        internal SyntheticConnection(bool saved = false)
        {
            Settings = new(directory);
            if (!saved) return;
            Settings.Save(Endpoint);
            Tokens.Current = new("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        }

        internal ClientConnection CreateConnection() => new(Settings, Tokens, new HttpClient(this), async uri =>
        {
            AuthorizationUri = uri;
            BrowserOpens++;
            if (FailBrowser) return false;
            if (CompleteLogin)
            {
                var query = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                // 実ブラウザーの代わりに、実ループバック受付へ合成の認可応答を送る。
                var callback = new HttpClient(new HttpClientHandler { UseProxy = false });
                CallbackTask = SendCallbackAsync(callback, new Uri(query["redirect_uri"] + "?state=" + Uri.EscapeDataString(query["state"]) + "&code=synthetic"));
                await Task.Yield();
            }
            return true;
        });

        private static async Task SendCallbackAsync(HttpClient callback, Uri uri)
        {
            using (callback)
            {
                // 接続失敗を握りつぶさず、UIが遷移しなければシナリオのタイムアウトで検出する。
                using var response = await callback.GetAsync(uri);
                response.EnsureSuccessStatusCode();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var origin = ApiEndpoint.Parse(request.RequestUri.GetLeftPart(UriPartial.Authority));
            if (!origin.Equals(Endpoint)) OtherServerRequests.Add(path);
            if (path == "/.well-known/openid-configuration" && ConnectionGate is not null)
            {
                try { await ConnectionGate.Task.WaitAsync(token); }
                catch (OperationCanceledException) { ConnectionCancelled = true; throw; }
            }
            if (path == "/.well-known/openid-configuration") return FailConnection ? new(HttpStatusCode.ServiceUnavailable) : Json(new
            {
                issuer = origin.Value, authorization_endpoint = origin.Resolve("connect/authorize"),
                token_endpoint = origin.Resolve("connect/token"), response_types_supported = new[] { "code" },
                code_challenge_methods_supported = new[] { "S256" }
            });
            if (path == "/connect/token") return RejectAuthentication ?
                new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) } : Json(new
            {
                access_token = "synthetic-access", refresh_token = "synthetic-refresh", token_type = "Bearer", expires_in = 900
            });
            if (request.Headers.Authorization?.Parameter != "synthetic-access") throw new InvalidOperationException("合成Bearerが必要です。");
            if (path == "/auth/me") return RejectAuthentication ? new(HttpStatusCode.Unauthorized) : Json(new
            {
                authUserId = Guid.Parse("493d9035-2de5-4235-921f-f79b799d828b"), displayName = "テスト利用者",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(2), verifiedAtUtc = DateTimeOffset.UtcNow
            });
            if (path == "/auth/logout") return new(FailLogout ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
            if (path == "/ledgers/open-last") return RateLimitOpening ? new(HttpStatusCode.TooManyRequests) :
                Json(new { ledger = HasPreviousLedger ? first : null });
            if (path == "/ledgers" && request.Method == HttpMethod.Get) return Json(new[] { first, second }.Take(ChoiceCount));
            if (path == "/ledgers" && request.Method == HttpMethod.Post)
            {
                Creations.Add(await request.Content!.ReadAsStringAsync(token));
                return FailCreation ? new(HttpStatusCode.ServiceUnavailable) : Json(first);
            }
            if (path == $"/ledgers/{second.LedgerId:N}/open") return Json(second);
            if (path == $"/ledgers/{first.LedgerId:N}/open") return Json(first);
            if (PeopleHandler is not null && path.Contains("/people", StringComparison.Ordinal))
                return await PeopleHandler(request, token);
            if (path == $"/ledgers/{first.LedgerId:N}/people") return Json(new PeopleInfo(
                [new(person, "父", null, null, 0, Guid.NewGuid())], person, "Admin", Guid.NewGuid()));
            throw new InvalidOperationException("想定しない合成API要求: " + path);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
