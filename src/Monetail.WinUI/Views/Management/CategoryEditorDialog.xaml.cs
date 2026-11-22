using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Presentation.Management;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Management;
public sealed partial class CategoryEditorDialog : ContentDialog
{
    public CategoriesManagementViewModel ViewModel { get; }

    private readonly FormKeyboard keyboard;
    private bool suspended;
    public CategoryEditorDialog(CategoriesManagementViewModel model)
    {
        ViewModel = model;
        InitializeComponent();
        CategoryFrom.IsCancellationTarget = CategoryTo.IsCancellationTarget = focused => focused is Button button && Equals(button.Content, CloseButtonText);
        keyboard = FormKeyboard.ForDialog(this, SaveShortcutAsync, CategoryName, CategoryFrom, CategoryTo);
    }

    private void CategoryDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        suspended = false;
        CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = false;
        if (!ViewModel.IsDeleting)
            CategoryName.Focus(FocusState.Programmatic);
    }

    private void CategoryDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = ViewModel.Context.IsBusy && !suspended;
        if (!args.Cancel)
            CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = true;
    }

    private async void SaveCategory(ContentDialog sender, ContentDialogButtonClickEventArgs args)
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
        var saved = await ViewModel.SaveEditorAsync(CategoryFrom.Complete(), CategoryTo.Complete());
        if (!saved && !suspended)
        {
            if (ViewModel.NameError is not null)
                CategoryName.Focus(FocusState.Programmatic);
            else if (ViewModel.PeriodError is not null)
                CategoryFrom.Focus(FocusState.Programmatic);
        }

        return saved;
    }

    private void UseLatestCategory(object sender, RoutedEventArgs args)
    {
        ViewModel.AdoptLatestCommand.Execute(null);
        if (!ViewModel.IsDeleting)
            CategoryName.Focus(FocusState.Programmatic);
    }

    // 401のPrimaryButton deferral中にShowAsyncの完了を待たない。
    public void Suspend()
    {
        suspended = true;
        CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = true;
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
