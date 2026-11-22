using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Management;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Management;
public sealed partial class ParticipantEditorDialog : ContentDialog
{
    public ParticipantsManagementViewModel ViewModel { get; }

    private readonly FormKeyboard keyboard;
    private bool suspended;
    public ParticipantEditorDialog(ParticipantsManagementViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        keyboard = FormKeyboard.ForDialog(this, SaveShortcutAsync);
    }

    private void ParticipantDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args) => args.Cancel = ViewModel.Context.IsBusy && !suspended;
    private async void SaveParticipant(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = keyboard.IsComposing || !await ViewModel.SaveEditorAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task SaveShortcutAsync()
    {
        if (!ViewModel.IsExcluding && !keyboard.IsComposing && await ViewModel.SaveEditorAsync())
            Hide();
    }

    private void UseLatestParticipant(object sender, RoutedEventArgs args) => ViewModel.AdoptLatestCommand.Execute(null);
    public void Suspend()
    {
        suspended = true;
        Hide();
    }

    public void Resuming() => suspended = false;
    public void Release()
    {
        Suspend();
        Bindings.StopTracking();
    }

    private bool Not(bool value) => !value;
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Hide(bool value) => Show(!value);
    private Visibility Error(string? value) => Show(!string.IsNullOrEmpty(value));
    private ContentDialogButton Default(bool excluding) => excluding ? ContentDialogButton.Close : ContentDialogButton.None;
}
