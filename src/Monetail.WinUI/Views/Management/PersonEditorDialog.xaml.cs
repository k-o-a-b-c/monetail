using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Management;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Management;
public sealed partial class PersonEditorDialog : ContentDialog
{
    public PeopleManagementViewModel ViewModel { get; }

    private readonly FormKeyboard keyboard;
    private bool suspended;
    public PersonEditorDialog(PeopleManagementViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        PersonValidFrom.IsCancellationTarget = PersonValidTo.IsCancellationTarget = focused => focused is Button button && Equals(button.Content, CloseButtonText);
        keyboard = FormKeyboard.ForDialog(this, SaveShortcutAsync, PersonName, PersonValidFrom, PersonValidTo);
    }

    private void PersonDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        suspended = false;
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = false;
        if (!ViewModel.IsDeleting)
            PersonName.Focus(FocusState.Programmatic);
    }

    private void PersonDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = ViewModel.Context.IsBusy && !suspended;
        if (!args.Cancel)
            PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = true;
    }

    private async void SavePerson(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await SaveAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task SaveShortcutAsync()
    {
        if (!ViewModel.IsDeleting && await SaveAsync())
            Hide();
    }

    private async Task<bool> SaveAsync()
    {
        if (keyboard.IsComposing || !ViewModel.CanSave)
            return false;
        var saved = await ViewModel.SaveEditorAsync(ViewModel.IncludesPeriod ? PersonValidFrom.Complete() : ViewModel.Editing?.ValidFrom, ViewModel.IncludesPeriod ? PersonValidTo.Complete() : ViewModel.Editing?.ValidTo);
        if (!saved && !suspended)
        {
            if (ViewModel.NameError is not null)
                PersonName.Focus(FocusState.Programmatic);
            else if (ViewModel.PeriodError is not null)
                PersonValidFrom.Focus(FocusState.Programmatic);
        }

        return saved;
    }

    private void UseLatestPerson(object sender, RoutedEventArgs args)
    {
        ViewModel.AdoptLatestCommand.Execute(null);
        if (!ViewModel.IsDeleting)
            PersonName.Focus(FocusState.Programmatic);
    }

    // 401のPrimaryButton deferral中にShowAsyncの完了を待たない。
    public void Suspend()
    {
        suspended = true;
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = true;
        Hide();
    }

    public void Release()
    {
        Suspend();
        Bindings.StopTracking();
    }

    private bool Not(bool value) => !value;
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Hide(bool value) => Show(!value);
    private Visibility Error(string? value) => Show(!string.IsNullOrEmpty(value));
    private ContentDialogButton Default(bool deletion) => deletion ? ContentDialogButton.Close : ContentDialogButton.None;
}
