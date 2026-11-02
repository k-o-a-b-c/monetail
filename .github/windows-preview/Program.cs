using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using Monetail.Windows;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Controls;

internal static class Program
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args.Length > 1)
        {
            AddFontResourceEx(Path.GetFullPath(args[1]), 0x10, IntPtr.Zero);
            Application.SetDefaultFont(new Font("Noto Sans CJK JP", 9));
        }
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var directory = Path.Combine(Path.GetTempPath(), "monetail-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            new ClientSettingsStore(directory).Save(endpoint);
            new FileTokenStore(directory, new DpapiDataProtector()).Save(endpoint,
                new TokenSet("preview-access", "preview-refresh", DateTimeOffset.UtcNow.AddMinutes(10)));
            using var api = new PreviewApi();
            using var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [directory, new HttpClient(api)], null)!;
            window.ClientSize = new Size(600, 680);
            window.Show();
            var initialize = (Task)typeof(MainWindow).GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            Wait(() => initialize.IsCompleted); initialize.GetAwaiter().GetResult();
            Capture(window, output, "01-home");
            var root = window.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single();
            root.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "人物管理").PerformClick();
            Wait(() => Controls(window).OfType<ListView>().Any());
            Capture(window, output, "02-people-list");
            var list = Controls(window).OfType<ListView>().Single();
            list.Items[0].Selected = true; Application.DoEvents(); Click(window, "編集");
            Capture(window, output, "03-people-edit");
            var from = Controls(window).OfType<DateInput>().Single(input => input.AccessibleName == "有効開始日");
            var to = Controls(window).OfType<DateInput>().Single(input => input.AccessibleName == "有効終了日");
            from.TimeProvider = new PreviewClock();
            from.Focus(); from.Text = "1011"; to.Focus(); Application.DoEvents();
            if (from.Text != "2026/10/11") throw new InvalidOperationException("日付の補完結果が想定と異なります。");
            Capture(window, output, "06-date-completion");
            Controls(window).OfType<TextBox>().Single(input => input.AccessibleName == "人物名").Text = "変更途中の名前";
            api.Conflict = true; Click(window, "保存");
            Capture(window, output, "04-people-conflict");
            Click(window, "キャンセル"); Click(window, "並び順を変更");
            list = Controls(window).OfType<ListView>().Single(); list.Items[1].Selected = true; Application.DoEvents();
            Capture(window, output, "05-people-order");
            window.Close();
        }
        finally { Directory.Delete(directory, true); }
    }

    private static IEnumerable<Control> Controls(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Controls(control)));
    private static void Wait(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
        if (!ready()) throw new InvalidOperationException("画面の表示を待てませんでした。");
    }
    private static void Click(MainWindow window, string text)
    {
        Controls(window).OfType<Button>().Single(button => button.Text == text).PerformClick();
        var root = window.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single();
        Wait(() => root.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "接続先の設定").Enabled);
    }
    private static void Capture(MainWindow window, string output, string name)
    {
        Application.DoEvents(); window.Refresh(); Application.DoEvents();
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"Captured {name}: {bitmap.Width}x{bitmap.Height}, DPI={window.DeviceDpi}");
    }

    private sealed class PreviewApi : HttpMessageHandler
    {
        private readonly Guid ledger = Guid.NewGuid();
        private readonly PersonInfo[] people = [new(Guid.NewGuid(), "母", new(2026, 4, 1), null, 0, Guid.NewGuid()),
            new(Guid.NewGuid(), "父", null, null, 1, Guid.NewGuid()), new(Guid.NewGuid(), "子供", null, new(2026, 9, 30), 2, Guid.NewGuid())];
        private readonly Guid order = Guid.NewGuid();
        public bool Conflict;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            var status = HttpStatusCode.OK;
            if (path == "/.well-known/openid-configuration") body = new
            {
                issuer = "https://synthetic-api.example/", authorization_endpoint = "https://synthetic-api.example/connect/authorize",
                token_endpoint = "https://synthetic-api.example/connect/token", response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { "S256" }
            };
            else if (path == "/auth/me") body = new { authUserId = Guid.NewGuid(), displayName = "サンプル利用者",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1), verifiedAtUtc = DateTimeOffset.UtcNow };
            else if (path == "/ledgers/open-last") body = new { ledger = new LedgerInfo(ledger, "家族の家計簿", "Admin", Guid.NewGuid().ToString("N")) };
            else if (request.Method == HttpMethod.Patch && Conflict)
            {
                status = HttpStatusCode.Conflict;
                body = new { code = "revision_conflict", latest = people[1] with { Name = "お父さん", ValidFrom = new(2026, 4, 1), Revision = Guid.NewGuid() } };
            }
            else if (path.EndsWith("/people")) body = new PeopleInfo(people, people[1].PersonId, "Admin", order);
            else throw new InvalidOperationException("撮影用APIの対象外です。");
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
        }
    }

    // 撮影日の違いで省略入力の補完結果が変わらないようにする。
    private sealed class PreviewClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
