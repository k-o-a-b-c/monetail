using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Management;
using Windows.ApplicationModel.DataTransfer;

namespace Monetail.WinUI.Views.Management;
public sealed partial class ParticipantsPage : Page, IManagementFeature
{
    public ParticipantsManagementViewModel ViewModel { get; private set; } = null!;

    private CancellationTokenSource? reads;
    private ContentDialog? dialog;
    private Task dialogTask = Task.CompletedTask;
    private bool suspended;
    public ParticipantsPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (ParticipantsManagementViewModel)args.Parameter;
        ViewModel.EditorRequested += OpenEditorAsync;
        reads = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.Context.Token);
        Bindings.Update();
    }

    public Task LoadAsync() => ViewModel.LoadAsync(reads!.Token);
    public bool CanStay => ViewModel.CanManage;
    public bool HasDialog => dialog is not null;
    public bool CanRestoreAuthentication => ViewModel.CanRestoreAuthentication;

    private Task OpenEditorAsync()
    {
        dialog ??= ViewModel.Cancelling is null ? new ParticipantEditorDialog(ViewModel) : new InvitationCancellationDialog(ViewModel);
        dialog.XamlRoot = XamlRoot;
        return dialogTask = ShowDialogAsync(dialog);
    }

    private async Task ShowDialogAsync(ContentDialog current)
    {
        try
        {
            await current.ShowAsync();
        }
        finally
        {
            if (!suspended && ReferenceEquals(dialog, current))
            {
                ReleaseDialog();
                ViewModel.CloseEditor();
                ViewModel.CloseCancellation();
            }
        }
    }

    private void ReleaseDialog()
    {
        if (dialog is ParticipantEditorDialog participant)
            participant.Release();
        if (dialog is InvitationCancellationDialog invitation)
            invitation.Release();
        dialog = null;
    }

    public void SuspendForAuthentication()
    {
        suspended = true;
        reads?.Cancel();
        if (dialog is ParticipantEditorDialog participant)
            participant.Suspend();
        else if (dialog is InvitationCancellationDialog invitation)
            invitation.Release();
    }

    public async Task ResumeAsync(string? denied, CancellationToken token)
    {
        await dialogTask;
        await ViewModel.ResumeAsync(denied, token);
        reads?.Dispose();
        reads = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.Context.Token);
        ViewModel.ActivateReads(reads.Token);
        suspended = false;
        if (dialog is ParticipantEditorDialog participant)
        {
            participant.Resuming();
            dialogTask = ShowDialogAsync(participant);
        }
    }

    private async void EditParticipant(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MemberInfo member } && ViewModel.EditCommand.CanExecute(member))
            await ViewModel.EditCommand.ExecuteAsync(member);
    }

    private async void ExcludeParticipant(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MemberInfo member } && ViewModel.ExcludeCommand.CanExecute(member))
            await ViewModel.ExcludeCommand.ExecuteAsync(member);
    }

    private async void CancelInvitation(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: LedgerInvitationInfo invitation } && ViewModel.CancelCommand.CanExecute(invitation))
            await ViewModel.CancelCommand.ExecuteAsync(invitation);
    }

    private void CopyInvitation(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: LedgerInvitationInfo invitation } || !ViewModel.CanCopy(invitation))
            return;
        try
        {
            var data = new DataPackage();
            data.SetText(invitation.Url);
            Clipboard.SetContent(data);
            ViewModel.Copied(true);
        }
        catch (Exception error)when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ViewModel.Copied(false);
        }
    }

    private void CloseNotice(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.ClearListError();
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Hide(bool value) => Show(!value);
    private Visibility Error(string? value) => Show(HasError(value));
    private bool HasError(string? value) => !string.IsNullOrEmpty(value);
    public void Release()
    {
        reads?.Cancel();
        reads?.Dispose();
        reads = null;
        ReleaseDialog();
        ViewModel.EditorRequested -= OpenEditorAsync;
        ViewModel.Leave();
        Bindings.StopTracking();
    }
}
