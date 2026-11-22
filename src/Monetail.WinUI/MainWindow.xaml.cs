using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Monetail.Windows.Client.Transport;
using Windows.Graphics;

namespace Monetail.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
        : this(ClientConnection.CreateDefault()) { }

    internal MainWindow(ClientConnection connection)
    {
        this.connection = connection;
        InitializeComponent();
        Title = "Monetail";
        SizeInitialWindow();
        Closed += CloseConnection;
        WorkspaceRoot.Children.Remove(EditorContents);
        InitializeReceipts();
        InitializeReceiptEditor();
        InitializeTransferEditor();
        ShowAuthentication()?.PrepareInitialDestination();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SizeInitialWindow()
    {
        // WinUIの目標サイズは論理ピクセル。AppWindow.Resizeは物理ピクセルなのでDPIで換算する。
        // この時点ではXamlRoot.RasterizationScaleが未確定のため、WindowのHWNDから直接取得する。
        var windowId = AppWindow.Id;
        var hwnd = Win32Interop.GetWindowFromWindowId(windowId);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new SizeInt32(
            Math.Min((int)Math.Round(1100 * scale), workArea.Width),
            Math.Min((int)Math.Round(780 * scale), workArea.Height)));
    }

    private void DisplayLedger(LedgerInfo ledger)
    {
        DiscardLedgerContents();
        DisplayLedgerHeader(ledger);
        NavigateWorkspace(WorkspaceScreen.HomePage);
    }

    private void DisplayLedgerHeader(LedgerInfo ledger)
    {
        currentLedger = ledger;
        LedgerName.Text = ledger.Name;
        LedgerSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, ledger.Name);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LedgerSelector, ledger.Name);
        LedgerSelector.IsEnabled = account is not null;

    }

    private async void ShowRecord(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen || !await LeaveRecordEditorAsync()) return;
        NavigateWorkspace(WorkspaceScreen.HomePage);
    }

    private async void ShowManagement(object sender, RoutedEventArgs e)
    {
        if (busy || ManagementDialogOpen || !await LeaveRecordEditorAsync()) return;
        NavigateWorkspace(WorkspaceScreen.ManagementPage);
        if (ManagementPage is { } page)
            await page.OpenSectionAsync(management!.PeopleEnabled ? Monetail.Windows.Presentation.Workspace.ManagementSection.People
                : Monetail.Windows.Presentation.Workspace.ManagementSection.Account);
    }
}
