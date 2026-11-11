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
        try
        {
            if (capture)
                Resources["ContentControlThemeFontFamily"] = new FontFamily("ms-appx:///Assets/PreviewFont.otf#Noto Sans CJK JP");
            window = new MainWindow();
            var root = (FrameworkElement)window.Content;
            ObserveDialogs(root);
            window.Activate();
            await Wait(() => root.IsLoaded);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var mother = new PersonInfo(Guid.NewGuid(), "母", today, null, 0, Guid.NewGuid());
            var father = new PersonInfo(Guid.NewGuid(), "父", null, null, 1, Guid.NewGuid());
            var child = new PersonInfo(Guid.NewGuid(), "子供", null, today.AddDays(-1), 2, Guid.NewGuid());
            window.DisplayWorkspace(new LedgerInfo(Guid.NewGuid(), "家族の家計簿", "Admin", Guid.NewGuid().ToString("N")),
                new PeopleInfo([mother, father, child], father.PersonId, "Admin", Guid.NewGuid()));
            await Settle();
            Check(Find<Grid>(root, "HomePage").Visibility == Visibility.Visible, "ホームの表示");
            if (capture) await Capture(root, output, "01-home");

            await InvokeAsync(() => Find<Button>(root, "SettingsButton"));
            await Settle();
            var navigation = Find<NavigationView>(root, "ManagementNavigation");
            var people = Find<ListView>(root, "PeopleList");
            Check(Find<Grid>(root, "ManagementPage").Visibility == Visibility.Visible, "管理・設定への切替");
            Check(navigation.IsPaneOpen && ReferenceEquals(navigation.SelectedItem,
                Find<NavigationViewItem>(root, "PeopleNavigation")), "左メニューを保持");
            Check(people.Items.Count == 2, "人物の初期表示は本日有効な人物のみ");
            Find<CheckBox>(root, "IncludeInactivePeople").IsChecked = true;
            Check(people.Items.Count == 3, "期間外表示を選ぶと全員表示");
            Check(((PersonRow)people.Items[0]).Name == "父（本人）", "本人先頭");
            Check(((PersonRow)people.Items[1]).ValidFrom == today.ToString("yyyy/MM/dd") &&
                ((PersonRow)people.Items[2]).ValidTo == today.AddDays(-1).ToString("yyyy/MM/dd"), "既存Clientの日付表示");
            Check(Find<TextBlock>(root, "LedgerContext").Text == "家族の家計簿", "家計簿名を表示");
            Check(!All(root).OfType<MenuBar>().Any(), "メニューバーなし");
            var paneBounds = navigation.PaneDisplayMode;
            Check(paneBounds == NavigationViewPaneDisplayMode.Left, "左メニューと右コンテンツを同時表示");
            if (capture) await Capture(root, output, "02-management-people");

            window.AppWindow.ResizeClient(new SizeInt32(900, 620));
            await Settle();
            Check(people.ActualWidth > 0 && people.ActualHeight > 0, "リサイズ後の一覧表示");
            var settingsArea = Find<Grid>(root, "PeopleContent");
            Check(settingsArea.ActualWidth >= 400, "リサイズ後の操作領域");
            if (capture) await Capture(root, output, "03-management-compact");

            root.RequestedTheme = ElementTheme.Dark;
            await Settle();
            Check(root.ActualTheme == ElementTheme.Dark, "標準のダークテーマ");
            if (capture) await Capture(root, output, "04-management-dark");
            root.RequestedTheme = ElementTheme.Default;

            await InvokeAsync(() => Find<Button>(root, "ReturnButton"));
            await Settle();
            Check(Find<Grid>(root, "HomePage").Visibility == Visibility.Visible, "同じウィンドウでホームへ戻る");
            var rasterizationScale = root.XamlRoot.RasterizationScale;
            await CheckInvocationGuards(root);
            // 調査時だけ対象の認証復帰を反復し、通常CIでは成功済みの確認を増やさない。
            var recoveryAttempts = arguments.Contains("--repeat-participant-recovery") ? 5 : 1;
            for (var attempt = 1; attempt <= recoveryAttempts; attempt++)
            {
                TraceInvocation($"participantRecoveryAttempt={attempt}; phase=begin");
                await CheckParticipantAuthenticationRecovery(includeInvitation: attempt == 1);
                TraceInvocation($"participantRecoveryAttempt={attempt}; phase=passed");
            }
            await CheckCategories();
            await CheckPeopleFilter();
            await CheckConnectionFlow(output, capture);
            await CheckPeopleFlow(output, capture);
            await CheckPersonDeletion();
            await CheckPersonOrder();
            await CheckPersonOrderReauthentication();
            await CheckPersonReauthentication();
            await CheckParticipantsAndInvitations();
            await CheckInputControls();
            await CheckPayments();
            await CheckReceiptList();
            await CheckReceiptNavigation();
            await CheckReceiptEditor();
            await CheckReceiptParentSelection();
            await CheckReceiptEditorOperations();
            await CheckReceiptEditorLoading();
            await CheckReceiptDetails();
            await CheckReceiptStoredDetails();
            await CheckReceiptMemos();
            File.WriteAllText(Path.Combine(output, "smoke-result.json"),
                JsonSerializer.Serialize(new { passed = true, data = "synthetic", screenshots = capture,
                    rasterizationScale, checks }));
            window?.Close();
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.ToString());
            window?.Close();
            Environment.Exit(1);
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
        return root.FindName(name) as T ?? throw new InvalidOperationException("コントロールが見つかりません: " + name);
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
