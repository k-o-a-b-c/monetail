using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monetail.Windows.Presentation.Management;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Views.Management;
public sealed partial class PaymentMethodsPage : Page, IManagementFeature
{
    public PaymentMethodsManagementViewModel ViewModel { get; private set; } = null!;

    private CancellationTokenSource? reads;
    private PaymentEditorDialog? dialog;
    private Task dialogTask = Task.CompletedTask;
    private bool suspended;
    public PaymentMethodsPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel = (PaymentMethodsManagementViewModel)args.Parameter;
        ViewModel.EditorRequested += OpenEditorAsync;
        ViewModel.PropertyChanged += ModelChanged;
        reads = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.Context.Token);
        Bindings.Update();
    }

    public Task LoadAsync() => ViewModel.LoadAsync(reads!.Token);
    public event EventHandler? ReturnRequested;
    public bool CanStay => ViewModel.CanView;
    public bool HasDialog => dialog is not null;
    public bool CanRestoreAuthentication => ViewModel.CanRestoreAuthentication;

    private async Task OpenEditorAsync()
    {
        dialog ??= new(ViewModel)
        {
            XamlRoot = XamlRoot
        };
        var current = dialog;
        dialogTask = ShowDialogAsync(current);
        await dialogTask;
    }

    private async Task ShowDialogAsync(PaymentEditorDialog current)
    {
        try
        {
            await current.ShowAsync();
        }
        finally
        {
            if (!suspended && ReferenceEquals(dialog, current))
            {
                current.Release();
                dialog = null;
                ViewModel.CloseEditor();
                if (!CanStay)
                    ReturnRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void SuspendForAuthentication()
    {
        suspended = true;
        reads?.Cancel();
        dialog?.Suspend();
    }

    public async Task ResumeAsync(string? denied, CancellationToken token)
    {
        await dialogTask;
        await ViewModel.ResumeAsync(denied, token);
        suspended = false;
        if (dialog is not null)
            dialogTask = ShowDialogAsync(dialog);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.SelectedOrder) && ViewModel.IsOrdering && PaymentOrderList is not null && ViewModel.SelectedOrder is { } row)
            PaymentOrderList.ScrollIntoView(row);
        if (args.PropertyName == nameof(ViewModel.IsOrdering) && !ViewModel.IsOrdering && !CanStay)
            ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenPaymentOrder(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: Guid owner } && ViewModel.OrderCommand.CanExecute(owner))
            ViewModel.OrderCommand.Execute(owner);
    }

    private void OrderLoaded(object sender, RoutedEventArgs args) => _ = new FormKeyboard(PaymentOrderContent, SavePaymentOrderButton, () => ViewModel.SaveOrderCommand.ExecuteAsync(null));
    private void CloseNotice(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.ClearListError();
    private void CloseError(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.ClearOrderNotice();
    private Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private Visibility Hide(bool value) => Show(!value);
    private Visibility Empty(int count) => Show(count == 0);
    private Visibility Error(string? value) => Show(HasError(value));
    private bool HasError(string? value) => !string.IsNullOrEmpty(value);
    private InfoBarSeverity Severity(bool informational) => informational ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
    public void Release()
    {
        reads?.Cancel();
        reads?.Dispose();
        reads = null;
        dialog?.Release();
        dialog = null;
        ViewModel.EditorRequested -= OpenEditorAsync;
        ViewModel.PropertyChanged -= ModelChanged;
        ViewModel.Leave();
        ReturnRequested = null;
        Bindings.StopTracking();
    }
}
