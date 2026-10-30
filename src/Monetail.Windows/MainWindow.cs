using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows;

/// <summary>接続・ログインの画面だけを受け持ち、認証情報の処理はClientへ委譲する。</summary>
public sealed partial class MainWindow : Form
{
    private readonly ClientSettingsStore settings;
    private readonly ITokenStore tokens;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient http;
    private readonly FlowLayoutPanel content = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, Padding = new Padding(24)
    };
    private readonly ToolStripMenuItem connectionMenu = new("接続先の設定");
    private ApiEndpoint? endpoint;
    private MonetailApiClient? api;
    private ClientSession? session;
    private CancellationTokenSource? browserCancellation;
    private bool busy;
    private readonly List<Button> temporarilyDisabled = [];

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monetail")) { }

    internal MainWindow(string directory, HttpClient? httpClient = null)
    {
        http = httpClient ?? MonetailApiClient.CreateHttpClient();
        settings = new ClientSettingsStore(directory);
        tokens = new FileTokenStore(directory, new DpapiDataProtector());
        Text = "Monetail";
        ClientSize = new Size(560, 320);
        MinimumSize = new Size(450, 300);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var menu = new MenuStrip();
        var root = new ToolStripMenuItem("メニュー");
        root.DropDownItems.Add(connectionMenu);
        root.DropDownItems.Add("終了", null, (_, _) => Close());
        menu.Items.Add(root);
        connectionMenu.Click += (_, _) => ShowConnection();
        Controls.Add(content);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    internal Task InitializeAsync() => RunAsync(async () =>
    {
        endpoint = settings.Load();
        if (endpoint is null) ShowConnection();
        else
        {
            SetClient(endpoint);
            await RestoreAsync();
        }
    }, () => InitializeAsync());

    private void SetClient(ApiEndpoint value)
    {
        api = new MonetailApiClient(value, http);
        session = new ClientSession(api, value, tokens);
        SetLedgerClient();
    }

    private async Task RestoreAsync()
    {
        // 起動時の接続確認から認証の復元までをまとめて10秒に収める。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
        await api!.CheckConnectionAsync(timeout.Token);
        var account = await session!.RestoreAsync(timeout.Token);
        if (account is null) ShowLogin();
        else await ShowWorkspaceAsync(account, timeout.Token);
    }

    private void ShowConnection(string? message = null)
    {
        Reset("接続先の設定", message ?? "サーバーのURLを入力してください。");
        var input = new TextBox { Width = 440, Text = endpoint?.Value ?? "", PlaceholderText = "https://…", AccessibleName = "サーバーURL" };
        content.Controls.Add(input);
        var connect = Button("接続", () => ConnectAsync(input.Text));
        AcceptButton = connect;
        if (session is not null) Button("キャンセル", () => RunAsync(RestoreAsync, () => InitializeAsync()));
        input.Focus();
    }

    private Task ConnectAsync(string input)
    {
        ApiEndpoint candidate;
        try { candidate = ApiEndpoint.Parse(input); }
        catch (FormatException) { ShowConnection("パスを含まないHTTPSのサーバーURLを入力してください。"); return Task.CompletedTask; }
        return RunAsync(async () =>
        {
            Reset("Monetail", "接続中…");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
            var candidateApi = new MonetailApiClient(candidate, http);
            await candidateApi.CheckConnectionAsync(timeout.Token);
            // 接続確認が成功するまでは現在の設定・認証情報を変更しない。
            settings.Save(candidate);
            if (!Equals(endpoint, candidate)) tokens.Clear();
            endpoint = candidate;
            SetClient(candidate);
            var account = await session!.RestoreAsync(timeout.Token);
            if (account is null) ShowLogin();
            else await ShowWorkspaceAsync(account, timeout.Token);
        }, () => ConnectAsync(input));
    }

    private void ShowLogin(string? message = null)
    {
        ClearLedgerState();
        Reset("Monetail", message);
        Button("ログイン", LoginAsync);
    }

    private Task LoginAsync() => RunAsync(async () =>
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        browserCancellation = cancellation;
        try
        {
            await using var login = new BrowserOAuthLogin(api!, endpoint!);
            Reset("Monetail", "ブラウザーでログインしてください。");
            var notice = new Label { AutoSize = true, MaximumSize = new Size(450, 0) };
            content.Controls.Add(notice);
            void OpenBrowser()
            {
                try { Process.Start(new ProcessStartInfo(login.AuthorizationUri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                { notice.Text = "ブラウザーを開けませんでした。「ブラウザーを開く」から再試行してください。"; }
            }
            AddButton("ブラウザーを開く", OpenBrowser);
            var cancel = AddButton("キャンセル", cancellation.Cancel);
            CancelButton = cancel;
            OpenBrowser();
            // 利用者のブラウザー操作には接続用の10秒制限を適用しない。
            var result = await login.CompleteAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Reset("Monetail", "接続中…");
            var account = await session!.AcceptLoginAsync(result, cancellation.Token);
            if (account is null) ShowLogin("ログインを続けられませんでした。もう一度ログインしてください。");
            else await ShowWorkspaceAsync(account, cancellation.Token);
            TryBringToFront();
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested && cancellation.IsCancellationRequested) { ShowLogin(); }
        catch (AuthenticationRejectedException) { ShowLogin("ログインできませんでした。もう一度ログインしてください。"); }
        finally { browserCancellation = null; }
    }, () => InitializeAsync());

    private async Task RunAsync(Func<Task> action, Func<Task> retry)
    {
        if (busy) return;
        busy = true;
        connectionMenu.Enabled = false;
        SetButtonsEnabled(false);
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { }
        catch (AuthenticationRejectedException) { ShowLogin("ログインを継続できません。もう一度ログインしてください。"); }
        catch (AuthenticationContextChangedException error) { ShowLogin(error.Message); }
        catch (LedgerRequestException error) { ShowLedgerFailure(error, retry); }
        catch (Exception error) when (error is ApiConnectionException or HttpRequestException or OperationCanceledException
            or IOException or UnauthorizedAccessException or CryptographicException or System.Net.Sockets.SocketException)
        {
            if (!IsDisposed) ShowFailure(retry, error is IOException or UnauthorizedAccessException or CryptographicException);
        }
        finally
        {
            busy = false;
            if (!IsDisposed) { connectionMenu.Enabled = true; SetButtonsEnabled(true); }
        }
    }

    private void ShowFailure(Func<Task> retry, bool storageFailure)
    {
        Reset("Monetail", storageFailure ? "設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。"
            : "サーバーに接続できませんでした。接続先と通信状態を確認してください。");
        Button("再試行", retry);
        AddButton("接続先の変更", () => ShowConnection());
        AddButton("終了", Close);
    }

    private void Reset(string heading, string? message)
    {
        AcceptButton = null;
        CancelButton = null;
        while (content.Controls.Count > 0) content.Controls[0].Dispose();
        content.Controls.Add(new Label { Text = heading, AutoSize = true, Font = new Font(Font.FontFamily, 16), Margin = new Padding(0, 0, 0, 18) });
        if (message is not null) content.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(0, 0, 0, 12) });
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = AddButton(text, () => { });
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button AddButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(130, 32) };
        button.Click += (_, _) => action();
        content.Controls.Add(button);
        return button;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        if (!enabled)
        {
            foreach (var button in content.Controls.OfType<Button>().Where(button => button.Enabled))
            {
                temporarilyDisabled.Add(button);
                button.Enabled = false;
            }
        }
        else
        {
            foreach (var button in temporarilyDisabled.Where(button => !button.IsDisposed)) button.Enabled = true;
            temporarilyDisabled.Clear();
        }
    }

    private void TryBringToFront()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (!SetForegroundWindow(Handle))
        {
            var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = Handle, Flags = 3, Count = 3 };
            FlashWindowEx(ref flash);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        lifetime.Cancel();
        browserCancellation?.Cancel();
        http.Dispose();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
