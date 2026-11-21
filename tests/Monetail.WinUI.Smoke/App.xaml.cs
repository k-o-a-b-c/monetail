using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Monetail.WinUI.Smoke;

public partial class App : Application
{
    private MainWindow? window;
    private static string? invokeTracePath;
    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var output = Path.GetFullPath(arguments[Array.IndexOf(arguments, "--output") + 1]);
        var capture = arguments.Contains("--capture");
        Directory.CreateDirectory(output);
        invokeTracePath = Path.Combine(output, "ui-invoke.txt");
        uiThreadId = Environment.CurrentManagedThreadId;
        var results = new List<SmokeScenarioResult>();
        void SaveResult(bool passed) => File.WriteAllText(Path.Combine(output, "smoke-result.json"),
            JsonSerializer.Serialize(new { passed, data = "synthetic", screenshots = capture,
                rasterizationScale, checks, scenarios = results }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        try
        {
            var catalog = Scenarios(output, capture, arguments.Contains("--repeat-participant-recovery"));
            var selected = SmokeScenarioExecution.Select(catalog, arguments);
            if (arguments.Contains("--list-scenarios"))
            {
                File.WriteAllText(Path.Combine(output, "scenario-list.json"),
                    JsonSerializer.Serialize(catalog.Select(s => new { name = s.Name, suite = s.Suite })));
                Environment.Exit(0); return;
            }
            if (capture)
                Resources["ContentControlThemeFontFamily"] = new FontFamily("ms-appx:///Assets/PreviewFont.otf#Noto Sans CJK JP");
            // 単独の入力コントロール試験が自分のWindowを閉じても、最終Window閉鎖でアプリを早期終了させない。
            window = new MainWindow();
            ObserveDialogs((FrameworkElement)window.Content);
            window.Activate();
            await Wait(() => ((FrameworkElement)window.Content).IsLoaded);
            SaveResult(false);
            foreach (var scenario in selected)
                await SmokeScenarioExecution.RunAsync(scenario, () => checks.Count, result =>
                {
                    results.Add(result); SaveResult(false);
                });
            SaveResult(true);
            window?.Close(); Environment.Exit(0);
        }
        catch (Exception error)
        {
            SaveResult(false);
            File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.ToString());
            window?.Close(); Environment.Exit(1);
        }
    }

    private readonly List<string> checks = [];
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks.Add(description);
    }
    private static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        if (name is "SavePersonButton" or "CancelPersonButton")
        {
            var dialog = (ContentDialog)root.FindName("PersonDialog");
            var text = name == "SavePersonButton" ? dialog.PrimaryButtonText : dialog.CloseButtonText;
            return All(dialog).OfType<T>().Where(element => element is Button button && Equals(button.Content, text)).OrderByDescending(element => element.IsLoaded).FirstOrDefault()
                ?? throw new InvalidOperationException("ダイアログのボタンが見つかりません: " + name);
        }
        return (root.FindName(name) ?? (root.FindName("TransfersView") as Monetail.WinUI.Views.Transfers.TransferListView)?.FindName(name) ?? (root.FindName("ReceiptsView") as Monetail.WinUI.Views.Receipts.ReceiptListView)?.FindName(name) ?? (root.FindName("TransferEntry") as Monetail.WinUI.Views.Transfers.TransferEditorView)?.FindName(name) ?? (root.FindName("ReceiptEntry") as Monetail.WinUI.Views.Receipts.ReceiptEditorView)?.FindName(name) ?? FindWorkspaceElement(root, name) ?? FindSharedRecordElement(root, name) ?? FindAuthenticationElement(root, name) ?? FindLedgerPageElement(root, name) ?? FindConnectionElement(root, name)) as T ?? throw new InvalidOperationException("コントロールが見つかりません: " + name);
    }
    private static object? FindWorkspaceElement(FrameworkElement root, string name)
    {
        var page = (root.FindName("WorkspaceHost") as Frame)?.Content as FrameworkElement;
        if (name == "HomePage" && page is Monetail.WinUI.Views.Workspace.RecordHomePage ||
            name == "ManagementPage" && page is Monetail.WinUI.Views.Workspace.ManagementPage ||
            name == "LedgerChoicePage" && page is Monetail.WinUI.Views.LedgerChoiceView) return page;
        return page?.FindName(name);
    }
    private static object? FindLedgerPageElement(FrameworkElement root, string name) =>
        ((root.FindName("LedgerPageHost") as ContentControl)?.Content as Frame)?.Content is FrameworkElement page ? page.FindName(name) : null;

    private static object? FindAuthenticationElement(FrameworkElement root, string name) =>
        ((root.FindName("AuthenticationHost") as ContentControl)?.Content as Frame)?.Content is FrameworkElement page
            ? page.FindName(name) : null;

    private static bool ConnectionSetupVisible(FrameworkElement root) =>
        ((root.FindName("AuthenticationHost") as ContentControl)?.Content as Frame)?.Content is Monetail.WinUI.Views.Connection.ConnectionSetupPage setup &&
            VisibleAncestors(setup);

    private static Monetail.WinUI.Views.Connection.ConnectionSettingsView ConnectionView(FrameworkElement root)
    {
        if (((root.FindName("AuthenticationHost") as ContentControl)?.Content as Frame)?.Content is Monetail.WinUI.Views.Connection.ConnectionSetupPage setup)
            return (Monetail.WinUI.Views.Connection.ConnectionSettingsView)((ContentControl)setup.FindName("SettingsHost")).Content;
        return (Monetail.WinUI.Views.Connection.ConnectionSettingsView)((ContentControl)root.FindName("ConnectionSettingsContent")).Content;
    }
    private static object? FindConnectionElement(FrameworkElement root, string name)
    {
        if (name is not ("ServerUrl" or "ConnectButton" or "ConnectionCancel"))
            return null;
        return ConnectionView(root).FindName(name);
    }
    private static object? FindSharedRecordElement(FrameworkElement root, string automationId)
    {
        // 移管した要素は共通ControlのNameScopeで取得し、元のAutomationIdも照合する。
        var localName = automationId is "NewReceiptButton" or "TransferRegisterPlaceholder" ? "Register"
            : automationId.StartsWith("Receipt", StringComparison.Ordinal) ? automationId[7..]
            : automationId.StartsWith("Transfer", StringComparison.Ordinal) ? automationId[8..] : automationId;
        foreach (var viewName in new[] { "ReceiptsView", "TransfersView" })
        {
            if ((root.FindName(viewName) ?? FindWorkspaceElement(root, viewName)) is not FrameworkElement view)
                continue;
            foreach (var name in new[] { "Panel", "Footer" })
                if (view.FindName(name) is FrameworkElement scope && scope.FindName(localName) is DependencyObject element &&
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element) == automationId)
                    return element;
        }
        return null;
    }
    private static bool EditorClosed(FrameworkElement root) =>
        Find<ScrollViewer>(root, "PersonEditorContent").Visibility == Visibility.Collapsed;
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in All(child)) yield return nested;
        }
    }
    private static async Task Wait(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }
    private static Task Settle() => Task.Delay(250);
    private static async Task Capture(FrameworkElement root, string output, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var folder = await StorageFolder.GetFolderFromPathAsync(output);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
