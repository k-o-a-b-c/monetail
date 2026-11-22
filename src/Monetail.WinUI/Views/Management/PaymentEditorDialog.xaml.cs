using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Management;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Management;
public sealed partial class PaymentEditorDialog : ContentDialog
{
    public PaymentMethodsManagementViewModel ViewModel { get; }

    private readonly FormKeyboard keyboard;
    private bool suspended;
    public PaymentEditorDialog(PaymentMethodsManagementViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        PaymentFrom.IsCancellationTarget = PaymentTo.IsCancellationTarget = focused => focused is Button button && Equals(button.Content, CloseButtonText);
        keyboard = FormKeyboard.ForDialog(this, SaveShortcutAsync, PaymentName, PaymentFrom, PaymentTo);
        model.SaveRequested += SaveShortcutAsync;
    }

    private void PaymentDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        suspended = false;
        PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = false;
        if (!ViewModel.IsDeleting)
            (ViewModel.ChooseOwner ? (Control)PaymentOwnerChoice : PaymentName).Focus(FocusState.Programmatic);
    }

    private void PaymentDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = ViewModel.Context.IsBusy && !suspended;
        if (!args.Cancel)
            PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = true;
    }

    private async void SavePayment(ContentDialog sender, ContentDialogButtonClickEventArgs args)
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
        var saved = await ViewModel.SaveEditorAsync(PaymentFrom.Complete(), PaymentTo.Complete());
        if (!saved && !suspended)
        {
            if (ViewModel.NameError is not null)
                PaymentName.Focus(FocusState.Programmatic);
            else if (ViewModel.PeriodError is not null)
                PaymentFrom.Focus(FocusState.Programmatic);
        }

        return saved;
    }

    private void UseLatestPayment(object sender, RoutedEventArgs args)
    {
        ViewModel.AdoptLatestCommand.Execute(null);
        if (!ViewModel.IsDeleting)
            PaymentName.Focus(FocusState.Programmatic);
    }

    // 401のPrimaryButton deferral中にShowAsyncの完了を待たない。
    public void Suspend()
    {
        suspended = true;
        PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = true;
        Hide();
    }

    public void Release()
    {
        Suspend();
        ViewModel.SaveRequested -= SaveShortcutAsync;
        Bindings.StopTracking();
    }

    private bool Not(bool value) => !value;
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Hide(bool value) => Show(!value);
    private Visibility Error(string? value) => Show(!string.IsNullOrEmpty(value));
    private ContentDialogButton Default(bool deletion) => deletion ? ContentDialogButton.Close : ContentDialogButton.None;
}
