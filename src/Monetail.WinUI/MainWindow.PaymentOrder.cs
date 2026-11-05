using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private List<PaymentMethodInfo>? orderedPayments;
    private Guid paymentOrderOwner, paymentOrderRevision;
    private bool paymentOrderDenied;
    private void OpenPaymentOrder(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || sender is not Button { Tag: Guid owner } || !CanManagePaymentOwner(owner) || paymentInfo is null) return;
        BeginPaymentOrder(owner, paymentInfo.OwnerOrderRevisions.Single(p => p.PersonId == owner).OrderRevision,
            paymentInfo.PaymentMethods.Where(r => r.OwnerPersonId == owner).OrderBy(r => r.DisplayOrder).ThenBy(r => r.PaymentMethodId.ToString("N"), StringComparer.Ordinal));
    }
    private void BeginPaymentOrder(Guid owner, Guid revision, IEnumerable<PaymentMethodInfo> rows)
    {
        paymentOrderOwner = owner; paymentOrderRevision = revision; orderedPayments = rows.ToList(); paymentOrderDenied = false;
        PaymentsContent.Visibility = Visibility.Collapsed; PaymentOrderContent.Visibility = Visibility.Visible;
        PaymentOrderOwner.Text = PaymentOwnerLabel(owner);
        PaymentOrderNotice.IsOpen = false; PaymentOrderNotice.Visibility = Visibility.Collapsed;
        RenderPaymentOrder(-1);
    }
    private void RenderPaymentOrder(int index)
    {
        PaymentOrderList.ItemsSource = orderedPayments?.Select(r => new PaymentRow(r)).ToArray(); PaymentOrderList.SelectedIndex = index;
        if (PaymentOrderList.SelectedItem is { } selected) PaymentOrderList.ScrollIntoView(selected);
        UpdatePaymentOrderActions();
    }
    private void UpdatePaymentOrderActions()
    {
        if (SavePaymentOrderButton is null) return;
        var allowed = !busy && orderedPayments is not null && !paymentOrderDenied && CanManagePaymentOwner(paymentOrderOwner);
        SavePaymentOrderButton.IsEnabled = allowed;
        MovePaymentUpButton.IsEnabled = allowed && PaymentOrderList.SelectedIndex > 0;
        MovePaymentDownButton.IsEnabled = allowed && PaymentOrderList.SelectedIndex >= 0 && PaymentOrderList.SelectedIndex < orderedPayments!.Count - 1;
        CancelPaymentOrderButton.IsEnabled = !busy;
    }
    private void SelectPaymentOrder(object sender, SelectionChangedEventArgs args) => UpdatePaymentOrderActions();
    private void MovePaymentUp(object sender, RoutedEventArgs args) => MovePayment(-1);
    private void MovePaymentDown(object sender, RoutedEventArgs args) => MovePayment(1);
    private void MovePayment(int direction)
    {
        if (busy || paymentOrderDenied || orderedPayments is null || !CanManagePaymentOwner(paymentOrderOwner)) return;
        var from = PaymentOrderList.SelectedIndex; var to = from + direction;
        if (from < 0 || to < 0 || to >= orderedPayments.Count) return;
        (orderedPayments[from], orderedPayments[to]) = (orderedPayments[to], orderedPayments[from]); RenderPaymentOrder(to);
    }
    private async void SavePaymentOrder(object sender, RoutedEventArgs args) => await SavePaymentOrderAsync();
    private async Task SavePaymentOrderAsync()
    {
        if (busy || !SavePaymentOrderButton.IsEnabled || orderedPayments is null) return;
        var owner = paymentOrderOwner; var revision = paymentOrderRevision; var ids = orderedPayments.Select(r => r.PaymentMethodId).ToArray();
        PaymentOrderNotice.IsOpen = false; PaymentOrderNotice.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            try
            {
                DisplayPayments(await Payments.SaveOrderAsync(owner, revision, ids, lifetime.Token));
                DiscardPaymentOrder(); PaymentsContent.Visibility = Visibility.Visible;
            }
            catch (PaymentMethodRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestOrder is { } latest && paymentInfo is not null)
                {
                    // 応答の対象所有者だけを置換し、他人物の行・順序版は取得済みのまま維持する。
                    paymentInfo = paymentInfo with {
                        PaymentMethods = paymentInfo.PaymentMethods.Where(r => r.OwnerPersonId != owner).Concat(latest.PaymentMethods).ToArray(),
                        OwnerOrderRevisions = paymentInfo.OwnerOrderRevisions.Select(p => p.PersonId == owner ? p with { OrderRevision = latest.OrderRevision } : p).ToArray()
                    };
                    RenderPayments(); BeginPaymentOrder(owner, latest.OrderRevision, latest.PaymentMethods);
                    ShowPaymentOrderNotice("ほかの操作で変更されています。未保存の並び順を破棄し、最新の内容に切り替えました。", InfoBarSeverity.Informational);
                }
                else
                {
                    ApplyPaymentFailureContext(error);
                    paymentOrderDenied = error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                    ShowPaymentOrderNotice(error.StatusCode switch {
                        HttpStatusCode.Forbidden => "並び順を保存する権限がありません。変更内容は保存されていません。",
                        HttpStatusCode.NotFound => "対象人物または家計簿を操作できません。変更内容は保存されていません。",
                        HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                        _ => "並び順を保存できませんでした。内容を確認してください。"
                    });
                }
                UpdatePaymentOrderActions();
            }
        }, SavePaymentOrderAsync);
    }
    private void CancelPaymentOrder(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        DiscardPaymentOrder(); if (!CanViewPayments) ShowPage(HomePage); else PaymentsContent.Visibility = Visibility.Visible;
    }
    private void DiscardPaymentOrder()
    {
        orderedPayments = null; paymentOrderOwner = paymentOrderRevision = Guid.Empty; paymentOrderDenied = false;
        PaymentOrderList.ItemsSource = null; PaymentOrderContent.Visibility = PaymentOrderNotice.Visibility = Visibility.Collapsed;
        PaymentOrderNotice.IsOpen = false; UpdatePaymentOrderActions();
    }
    private void ShowPaymentOrderNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        PaymentOrderNotice.Message = message; PaymentOrderNotice.Severity = severity;
        PaymentOrderNotice.Visibility = Visibility.Visible; PaymentOrderNotice.IsOpen = true;
    }
}
