using Microsoft.UI.Xaml;

namespace Monetail.WinUI;

public partial class App : Application
{
    private MainWindow? window;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        await window.StartAsync();
    }
}
