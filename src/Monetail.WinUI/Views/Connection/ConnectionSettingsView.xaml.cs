using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Presentation.Connection;

namespace Monetail.WinUI.Views.Connection;

public sealed partial class ConnectionSettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel),
        typeof(ConnectionSettingsViewModel), typeof(ConnectionSettingsView), new PropertyMetadata(null,
            (owner, args) => ((ConnectionSettingsView)owner).SetModel(args.OldValue as ConnectionSettingsViewModel, args.NewValue as ConnectionSettingsViewModel)));
    public ConnectionSettingsViewModel ViewModel
    {
        get => (ConnectionSettingsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public ConnectionSettingsView()
    {
        InitializeComponent();
        _ = new FormKeyboard(this, null, null, ServerUrl);
        Loaded += (_, _) => ServerUrl.Focus(FocusState.Programmatic);
        Unloaded += (_, _) =>
        {
            if (ViewModel is { } model)
            {
                model.PropertyChanged -= ModelChanged;
                model.Dispose();
            }
        };
    }
    public ConnectionSettingsView(ConnectionSettingsViewModel model) : this() => ViewModel = model;
    private void SetModel(ConnectionSettingsViewModel? previous, ConnectionSettingsViewModel? current)
    {
        if (previous is not null)
            previous.PropertyChanged -= ModelChanged;
        if (current is not null)
            current.PropertyChanged += ModelChanged;
        Bindings.Update();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.UrlError) && ViewModel.UrlError is not null)
            ServerUrl.Focus(FocusState.Programmatic);
    }
    private bool Not(bool value) => !value;
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility ShowError(string? value) => Show(value is not null);
}
