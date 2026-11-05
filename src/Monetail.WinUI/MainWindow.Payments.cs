using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private PaymentMethodsInfo? paymentInfo;
    private PeopleInfo? paymentPeople;
    private PeoplePermissions? paymentPermissions;
    private PaymentMethodInfo? editingPayment, latestPayment;
    private PaymentMethodCreationDraft? paymentDraft;
    private readonly HashSet<Guid> missingPayments = [];
    private bool paymentDialogOpen, paymentDialogClosing, forcePaymentClose, deletingPayment, paymentEditorDenied;
    private Task paymentDialogTask = Task.CompletedTask;
    private FormKeyboard paymentKeyboard = null!;
    private PaymentMethodsWorkspace Payments => new(connection.Api, connection.Session, currentLedger!.LedgerId);
    private bool CanViewPayments => paymentPermissions is null || paymentPermissions.CurrentRole is not null;
    private bool CanManagePaymentOwner(Guid owner) => account is not null && paymentInfo is not null &&
        paymentInfo.OwnerOrderRevisions.Any(p => p.PersonId == owner) &&
        (paymentPermissions?.CurrentRole == "Admin" || paymentPermissions?.CurrentRole == "Editor" && paymentPermissions.MyPersonId == owner);
    private bool CanAddPayment => paymentInfo?.OwnerOrderRevisions.Any(p => CanManagePaymentOwner(p.PersonId)) == true;
    private bool CanChangePayment(PaymentMethodInfo row) => !missingPayments.Contains(row.PaymentMethodId) && CanManagePaymentOwner(row.OwnerPersonId);
    private string PaymentOwnerLabel(Guid owner) => paymentPeople?.People.FirstOrDefault(p => p.PersonId == owner)?.Name ?? "人物情報を確認できません";
    private Guid? ChosenPaymentOwner => editingPayment?.OwnerPersonId ??
        (paymentPermissions?.CurrentRole == "Editor" ? paymentPermissions.MyPersonId : (PaymentOwnerChoice.SelectedItem as PersonRow)?.Person.PersonId);

    private void InitializePayments()
    {
        WorkspaceRoot.Children.Remove(PaymentDialog);
        PaymentFrom.IsCancellationTarget = PaymentTo.IsCancellationTarget = focused => focused is Button b && Equals(b.Content, PaymentDialog.CloseButtonText);
        paymentKeyboard = FormKeyboard.ForDialog(PaymentDialog, SavePaymentAsync, PaymentName, PaymentFrom, PaymentTo);
        _ = new FormKeyboard(PaymentOrderContent, SavePaymentOrderButton, SavePaymentOrderAsync);
    }
    private void ResetPayments()
    {
        DiscardPaymentEditor(); DiscardPaymentOrder();
        paymentInfo = null; paymentPeople = null; paymentPermissions = null; missingPayments.Clear();
        PaymentsList.ItemsSource = null; IncludeInactivePayments.IsChecked = false; UpdatePaymentActions();
    }
    private Task LoadPaymentsAsync() => account is null || currentLedger is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var people = await new PeopleWorkspace(connection.Api, connection.Session, currentLedger.LedgerId).ListAsync(lifetime.Token);
        var payments = await Payments.ListAsync(lifetime.Token);
        paymentPeople = people; DisplayPayments(payments);
    }, LoadPaymentsAsync);
    private void DisplayPayments(PaymentMethodsInfo info)
    {
        paymentInfo = info; paymentPermissions = new(info.CurrentRole, info.MyPersonId); peoplePermissions = paymentPermissions;
        missingPayments.Clear(); RenderPayments(); UpdatePeopleActions();
    }
    private void RenderPayments(Guid? selection = null)
    {
        if (paymentInfo is null) { UpdatePaymentActions(); return; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var groups = paymentInfo.OwnerOrderRevisions.OrderByDescending(p => p.PersonId == paymentPermissions?.MyPersonId)
            .Select(p => new PaymentGroup(p.PersonId, PaymentOwnerLabel(p.PersonId), CanManagePaymentOwner(p.PersonId),
                paymentInfo.PaymentMethods.Where(r => r.OwnerPersonId == p.PersonId && (IncludeInactivePayments.IsChecked == true ||
                    (r.ValidFrom is null || r.ValidFrom <= today) && (r.ValidTo is null || r.ValidTo >= today)))
                .OrderBy(r => r.DisplayOrder).ThenBy(r => r.PaymentMethodId.ToString("N"), StringComparer.Ordinal).Select(r => new PaymentRow(r))))
            .Where(g => g.Count != 0).ToArray();
        var source = new CollectionViewSource { IsSourceGrouped = true, Source = groups };
        PaymentsList.ItemsSource = source.View;
        if (selection is not null) PaymentsList.SelectedItem = groups.SelectMany(g => g).FirstOrDefault(r => r.Payment.PaymentMethodId == selection);
        EmptyPayments.Visibility = groups.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePaymentActions();
    }
    private void FilterPayments(object sender, RoutedEventArgs args) => RenderPayments();
    private void SelectPayment(object sender, SelectionChangedEventArgs args) => UpdatePaymentActions();
    private void SelectPaymentOwner(object sender, SelectionChangedEventArgs args) => UpdatePaymentActions();
    private void UpdatePaymentActions()
    {
        if (AddPaymentButton is null || PaymentDialog is null || SavePaymentOrderButton is null) return;
        AddPaymentButton.IsEnabled = !busy && CanAddPayment;
        EditPaymentButton.IsEnabled = DeletePaymentButton.IsEnabled = !busy && PaymentsList.SelectedItem is PaymentRow row && CanChangePayment(row.Payment);
        PaymentDialog.IsPrimaryButtonEnabled = !busy && !paymentEditorDenied &&
            (editingPayment is { } edit ? CanChangePayment(edit) : ChosenPaymentOwner is { } owner && CanManagePaymentOwner(owner));
        UpdatePaymentOrderActions();
    }
    private void AddPayment(object sender, RoutedEventArgs args) { if (!busy && CanAddPayment) OpenPaymentEditor(null); }
    private void EditPayment(object sender, RoutedEventArgs args)
    { if (!busy && PaymentsList.SelectedItem is PaymentRow row && CanChangePayment(row.Payment)) OpenPaymentEditor(row.Payment); }
    private void DeletePayment(object sender, RoutedEventArgs args)
    { if (!busy && PaymentsList.SelectedItem is PaymentRow row && CanChangePayment(row.Payment)) OpenPaymentEditor(row.Payment, true); }
    private void OpenPaymentEditor(PaymentMethodInfo? row, bool deletion = false, Guid? owner = null)
    {
        if (ManagementDialogOpen || orderedPayments is not null) return;
        editingPayment = row; latestPayment = null; paymentDraft = null; deletingPayment = deletion; paymentEditorDenied = false;
        PaymentDialog.Title = deletion ? "支払方法の削除" : row is null ? "支払方法の追加" : "支払方法の編集";
        PaymentDialog.PrimaryButtonText = deletion ? "削除" : row is null ? "登録" : "保存";
        PaymentDialog.DefaultButton = deletion ? ContentDialogButton.Close : ContentDialogButton.None;
        PaymentFields.Visibility = deletion ? Visibility.Collapsed : Visibility.Visible;
        PaymentDeleteNotice.Visibility = deletion ? Visibility.Visible : Visibility.Collapsed;
        var choose = row is null && paymentPermissions?.CurrentRole == "Admin";
        PaymentOwnerChoice.Visibility = choose ? Visibility.Visible : Visibility.Collapsed;
        PaymentOwnerFixed.Visibility = choose ? Visibility.Collapsed : Visibility.Visible;
        PaymentOwnerChoice.ItemsSource = paymentPeople?.People.Where(p => paymentInfo?.OwnerOrderRevisions.Any(o => o.PersonId == p.PersonId) == true)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.PersonId).Select(p => new PersonRow(p, p.PersonId == paymentPermissions?.MyPersonId)).ToArray();
        PaymentOwnerChoice.SelectedItem = owner is null ? null : (PaymentOwnerChoice.ItemsSource as PersonRow[])?.FirstOrDefault(p => p.Person.PersonId == owner);
        PaymentOwnerName.Text = PaymentOwnerLabel(row?.OwnerPersonId ?? paymentPermissions?.MyPersonId ?? Guid.Empty);
        FillPaymentFields(row);
        PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = false;
        PaymentConflict.Visibility = PaymentRestoredNotice.Visibility = Visibility.Collapsed;
        PaymentEditorContent.Visibility = Visibility.Visible;
        ClearPaymentErrors(); UpdatePaymentDeleteNotice(); UpdatePaymentActions();
        paymentDialogTask = ShowPaymentDialogAsync();
    }
    private void FillPaymentFields(PaymentMethodInfo? row)
    {
        PaymentName.Text = row?.Name ?? "";
        PaymentFrom.Text = DateTimeInput.FormatDate(row?.ValidFrom); PaymentTo.Text = DateTimeInput.FormatDate(row?.ValidTo);
    }
    private async Task ShowPaymentDialogAsync()
    {
        paymentDialogOpen = true; paymentDialogClosing = forcePaymentClose = false; PaymentDialog.XamlRoot = WorkspaceRoot.XamlRoot;
        try { await PaymentDialog.ShowAsync(); }
        finally
        {
            paymentDialogOpen = paymentDialogClosing = false;
            if (!closed) { ClearPaymentEditor(); if (!forcePaymentClose && !CanViewPayments) ShowPage(HomePage); }
        }
    }
    private void PaymentDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (!deletingPayment) (PaymentOwnerChoice.Visibility == Visibility.Visible ? (Control)PaymentOwnerChoice : PaymentName).Focus(FocusState.Programmatic);
    }
    private void PaymentDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (busy && !forcePaymentClose && !closed) { args.Cancel = true; return; }
        paymentDialogClosing = true; PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = true;
    }
    private async void SavePayment(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; var deferral = args.GetDeferral();
        try { args.Cancel = !await TrySavePaymentAsync(); } finally { deferral.Complete(); }
    }
    private async Task SavePaymentAsync() { if (!deletingPayment && await TrySavePaymentAsync()) PaymentDialog.Hide(); }
    private async Task<bool> TrySavePaymentAsync()
    {
        if (busy || paymentKeyboard.IsComposing || !PaymentDialog.IsPrimaryButtonEnabled) return false;
        ClearPaymentErrors(); PaymentMethodInput? input = null;
        if (!deletingPayment)
        {
            try { input = new(PaymentName.Text, PaymentFrom.Complete(), PaymentTo.Complete()); }
            catch (PaymentMethodInputException error) { ShowPaymentError(error.Field, error.Message); return false; }
        }
        if (editingPayment is null && (paymentDraft?.Input != input || paymentDraft.OwnerPersonId != ChosenPaymentOwner))
            paymentDraft = new(ChosenPaymentOwner!.Value, input!);
        var saved = false;
        await RunAsync(async () =>
        {
            try
            {
                var result = deletingPayment ? await Payments.DeleteAsync(editingPayment!, lifetime.Token) : editingPayment is null
                    ? (await Payments.CreateAsync(paymentDraft!, lifetime.Token)).State
                    : await Payments.UpdateAsync(editingPayment, new(input!.Name, new(input.ValidFrom), new(input.ValidTo)), lifetime.Token);
                DisplayPayments(result); saved = true;
            }
            catch (PaymentMethodRequestException error) { HandlePaymentFailure(error); }
        }, SavePaymentAsync);
        return saved;
    }
    private void HandlePaymentFailure(PaymentMethodRequestException error)
    {
        if (error.Code == "revision_conflict" && error.LatestPaymentMethod is { } latest)
        {
            latestPayment = latest;
            LatestPaymentOwner.Text = PaymentOwnerLabel(latest.OwnerPersonId); LatestPaymentName.Text = latest.Name;
            LatestPaymentFrom.Text = DateTimeInput.FormatDate(latest.ValidFrom); LatestPaymentTo.Text = DateTimeInput.FormatDate(latest.ValidTo);
            PaymentConflict.Visibility = Visibility.Visible;
        }
        else
        {
            if (error.LatestPaymentMethods is { } collection) DisplayPayments(collection);
            ApplyPaymentFailureContext(error);
            if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) paymentEditorDenied = true;
            ShowPaymentError(error.Field, error.StatusCode switch
            {
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => "対象は削除されているか、この家計簿を操作できません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "payment_method_referenced" => "参照が残っているため削除できません。使用を終了する場合は有効終了日を設定してください。",
                    "duplicate_payment_method_name" => "この人物には同じ名前の支払方法が登録されています。",
                    "created_result_deleted" => "作成した支払方法は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして追加し直してください。",
                    "revision_conflict" => "ほかの操作で変更されています。入力を確認して再試行してください。",
                    _ => "支払方法の操作を完了できませんでした。入力内容を確認してください。"
                }
            });
        }
        UpdatePaymentActions();
    }
    private void ApplyPaymentFailureContext(PaymentMethodRequestException error)
    {
        if (error.Permissions is { } permission) paymentPermissions = permission;
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) paymentPermissions = new(null, null);
        if (error.StatusCode == HttpStatusCode.NotFound && editingPayment is { } row) missingPayments.Add(row.PaymentMethodId);
        peoplePermissions = paymentPermissions; RenderPayments(); UpdatePeopleActions();
    }
    private void UseLatestPayment(object sender, RoutedEventArgs args)
    {
        if (busy || latestPayment is null) return;
        editingPayment = latestPayment; latestPayment = null; FillPaymentFields(editingPayment); UpdatePaymentDeleteNotice();
        PaymentConflict.Visibility = Visibility.Collapsed; ClearPaymentErrors(); UpdatePaymentActions();
    }
    private void UpdatePaymentDeleteNotice() => PaymentDeleteNotice.Text = $"「{PaymentOwnerLabel(editingPayment?.OwnerPersonId ?? Guid.Empty)}」の「{editingPayment?.Name}」を削除しますか。参照がある支払方法は削除できません。";
    private void DiscardPaymentEditor()
    {
        forcePaymentClose = true; if (paymentDialogOpen) PaymentDialog.Hide(); ClearPaymentEditor();
    }
    private void ClearPaymentEditor()
    {
        PaymentFrom.SuppressCompletion = PaymentTo.SuppressCompletion = true;
        PaymentEditorContent.Visibility = Visibility.Collapsed; editingPayment = latestPayment = null; paymentDraft = null;
        deletingPayment = paymentEditorDenied = false; ClearPaymentErrors(); UpdatePaymentActions();
    }
    private void ClearPaymentErrors() => PaymentNameError.Visibility = PaymentPeriodError.Visibility = PaymentSaveError.Visibility = Visibility.Collapsed;
    private void ShowPaymentError(string? field, string message)
    {
        var target = field == "name" ? PaymentNameError : field is "validFrom" or "validTo" ? PaymentPeriodError : PaymentSaveError;
        target.Text = message; target.Visibility = Visibility.Visible;
    }
}
