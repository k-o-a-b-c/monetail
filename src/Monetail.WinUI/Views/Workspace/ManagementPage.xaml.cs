using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Workspace;

namespace Monetail.WinUI.Views.Workspace;
public sealed partial class ManagementPage : Page
{
    public ManagementViewModel ViewModel { get; private set; } = null!;

    public event RoutedEventHandler? BackRequested;
    public ManagementPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (ManagementViewModel)args.Parameter;
        ViewModel.PropertyChanged += ModelChanged;
        Bindings.Update();
        RenderSelection();
    }

    public void SetDetails(UIElement? content) => DetailsHost.Content = content;
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ManagementViewModel.SelectedSection))
            RenderSelection();
    }

    private void RenderSelection() => ManagementNavigation.SelectedItem = ManagementNavigation.MenuItems.OfType<NavigationViewItem>().Single(item => Equals(item.Tag, ViewModel.SelectedSection.ToString()));
    private async void SelectManagement(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ViewModel is null || args.SelectedItem is not NavigationViewItem { Tag: string value } || !Enum.TryParse<ManagementSection>(value, out var section) || section == ViewModel.SelectedSection)
            return;
        if (!ViewModel.SelectCommand.CanExecute(section))
        {
            RenderSelection();
            return;
        }

        await ViewModel.SelectCommand.ExecuteAsync(section);
    }

    private void ReturnToRecord(object sender, RoutedEventArgs args) => BackRequested?.Invoke(this, args);
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public void Release()
    {
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= ModelChanged;
        Bindings.StopTracking();
        DetailsHost.Content = null;
        BackRequested = null;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Release();
        base.OnNavigatedFrom(args);
    }
}
