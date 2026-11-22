using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Management;

namespace Monetail.WinUI.Views.Management;

public sealed partial class InvitationCancelDialog : ContentDialog
{
    public ParticipantsManagementViewModel ViewModel { get; }
    private bool closing;
    public InvitationCancelDialog(ParticipantsManagementViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
    }
    private void InvitationDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args) => args.Cancel = ViewModel.Context.IsBusy && !closing;
    private async void ConfirmCancelInvitation(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try { args.Cancel = !await ViewModel.CancelInvitationAsync(); }
        finally { deferral.Complete(); }
    }
    public void Release() { closing = true; Hide(); Bindings.StopTracking(); }
    private bool Not(bool value) => !value;
    private Visibility Error(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
}
