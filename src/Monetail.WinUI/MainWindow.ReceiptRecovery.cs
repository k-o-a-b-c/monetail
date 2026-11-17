using System.Net;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    // 入力値はコピーしない。連続401でも最初の編集位置と元のオブジェクトを使う。
    private sealed record ReceiptAuthentication(Guid AccountId, Guid LedgerId, string ApiOrigin, Control? Focus,
        int SelectionStart, int SelectionLength, (ScrollViewer Viewer, double X, double Y)[] Scrolls);
    private enum ReceiptRecoveryPhase { None, Login, Loading, Failed, Forbidden, Unavailable }
    private ReceiptAuthentication? receiptAuthentication;
    private ReceiptRecoveryPhase receiptRecoveryPhase;
    private CancellationTokenSource? receiptRecoveryLoading;
    private bool ReceiptRecoveryReadOnly => receiptRecoveryPhase != ReceiptRecoveryPhase.None;
    // 非表示/未レイアウト時にも必ず反映する。入力欄の制御にVisualTreeの実体化を使わない。
    private TextBox[] ReceiptTextInputs => [ReceiptDate, ReceiptTime, ReceiptShopName, ReceiptShopLocation, ReceiptInterval,
        ReceiptMemo, ReceiptCategoryMemo, ReceiptItemName, ReceiptItemPrice, ReceiptItemQuantity, ReceiptItemDiscount, ReceiptItemRateDiscount, ReceiptItemMemo];

    private static IEnumerable<DependencyObject> ReceiptVisuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ReceiptVisuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    // 共通操作が画面を無効化する前にも呼び、元のフォーカスを失わないようにする。
    private ReceiptAuthentication? RememberReceiptAuthenticationView()
    {
        // TextChangedの遅延があっても、通常離脱と同じ既存バッファへ未確定入力を同期する。
        if (receiptEditor is not null && receiptRecoveryPhase == ReceiptRecoveryPhase.None)
        { SyncReceiptHeader(); SyncReceiptGroupMemo(); SyncReceiptDetailInput(); }
        if (receiptEditor is not null && account is not null && currentLedger is not null && connection.Endpoint is not null &&
            (receiptAuthentication is not { } previous || previous.AccountId != account.AuthUserId ||
                previous.LedgerId != currentLedger.LedgerId || previous.ApiOrigin != connection.Endpoint.Value))
        {
            var focus = FocusManager.GetFocusedElement(WorkspaceRoot.XamlRoot) as Control;
            receiptAuthentication = new(account.AuthUserId, currentLedger.LedgerId, connection.Endpoint.Value, focus,
                (focus as TextBox)?.SelectionStart ?? 0, (focus as TextBox)?.SelectionLength ?? 0,
                ReceiptVisuals(ReceiptEditorPage).OfType<ScrollViewer>().Select(v => (v, v.HorizontalOffset, v.VerticalOffset)).ToArray());
        }
        return receiptAuthentication;
    }

    private bool SuspendReceiptForAuthentication()
    {
        if (receiptEditor is null || RememberReceiptAuthenticationView() is not { } context || context.ApiOrigin != connection.Endpoint?.Value) return false;
        receiptRecoveryPhase = ReceiptRecoveryPhase.Login;
        receiptRecoveryLoading?.Cancel();
        ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = true;
        if (receiptDialogOpen) ReceiptConfirmationDialog.Hide();
        receiptState?.Suspend(); transferState?.Suspend();
        connection.Masters.Clear(); connection.Session.TaxRates.Clear(); receiptTaxRates = null;
        RenderReceiptRecovery();
        return true;
    }

    private void DiscardReceiptAuthentication()
    {
        ResetReceiptEditor();
        receiptEditorRendering = receiptDetailRendering = true;
        try
        {
            foreach (var text in ReceiptTextInputs) text.Text = "";
            ReceiptCategories.ItemsSource = ReceiptDetailsList.ItemsSource = null;
            ReceiptDetailPeople.Children.Clear(); ReceiptItemTax.Items.Clear();
            ReceiptEditorPayment.ItemsSource = ReceiptCategoryChoice.ItemsSource = null;
        }
        finally { receiptEditorRendering = receiptDetailRendering = false; }
    }

    private async Task<bool> ResumeReceiptEditAsync(CancellationToken token)
    {
        if (receiptAuthentication is not { } context || receiptRecoveryPhase != ReceiptRecoveryPhase.Login || account is null) return false;
        if (context.AccountId != account.AuthUserId || context.ApiOrigin != connection.Endpoint?.Value || currentLedger?.LedgerId != context.LedgerId)
        { DiscardLedgerContents(); ClearLedger(); return false; }
        receiptRecoveryPhase = ReceiptRecoveryPhase.Loading;
        if (currentLedger is { } ledger) DisplayLedgerHeader(ledger);
        ShowPage(HomePage); RenderReceiptRecovery();
        await LoadReceiptRecoveryMastersAsync(context, token);
        return true;
    }

    private async void RetryReceiptMasters(object sender, RoutedEventArgs args)
    {
        if (receiptRecoveryPhase == ReceiptRecoveryPhase.Failed && receiptAuthentication is { } context)
            await LoadReceiptRecoveryMastersAsync(context, lifetime.Token);
    }

    private async Task LoadReceiptRecoveryMastersAsync(ReceiptAuthentication context, CancellationToken token)
    {
        if (receiptEditor is not { } editor || receiptRecoveryLoading is not null) return;
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
        receiptRecoveryLoading = loading; receiptRecoveryPhase = ReceiptRecoveryPhase.Loading;
        var api = connection.Api; var session = connection.Session;
        bool Current() => !closed && !loading.IsCancellationRequested && ReferenceEquals(receiptAuthentication, context) &&
            ReferenceEquals(editor, receiptEditor) && receiptRecoveryPhase == ReceiptRecoveryPhase.Loading &&
            account?.AuthUserId == context.AccountId && connection.Endpoint?.Value == context.ApiOrigin && currentLedger?.LedgerId == context.LedgerId;
        RenderReceiptRecovery(); connection.Masters.Clear(); session.TaxRates.Clear(); receiptTaxRates = null;
        try
        {
            var ledger = await connection.Ledgers.OpenAsync(context.LedgerId, loading.Token);
            if (!Current()) return;
            var people = await new PeopleWorkspace(api, session, context.LedgerId).ListAsync(loading.Token);
            if (!Current()) return;
            var categories = await new CategoriesWorkspace(api, session, context.LedgerId).ListAsync(loading.Token);
            if (!Current()) return;
            var taxes = await new TaxRatesWorkspace(api, session, context.LedgerId).ListAsync(loading.Token);
            if (!Current()) return;
            // 本人対応とロールを含む支払方法を最後に取得し、その応答で編集権限を判断する。
            var payments = await new PaymentMethodsWorkspace(api, session, context.LedgerId).ListAsync(loading.Token);
            if (!Current()) return;
            // マスタがすべて揃うまでは空データで選択肢や入力を置換しない。
            receiptEditorRendering = receiptDetailRendering = true;
            try
            {
                connection.Masters.UpdatePeople(context.LedgerId, people);
                connection.Masters.UpdatePayments(context.LedgerId, payments);
                connection.Masters.UpdateCategories(context.LedgerId, categories);
                receiptTaxRates = taxes; DisplayLedgerHeader(ledger with { Role = payments.CurrentRole });
            }
            finally { receiptEditorRendering = receiptDetailRendering = false; }
            var canEdit = payments.CurrentRole == "Admin" || payments.CurrentRole == "Editor" && payments.MyPersonId is Guid person &&
                (editor.ReceiptId is null || payments.PaymentMethods.Any(p => p.PaymentMethodId == editor.SavedPaymentMethodId && p.OwnerPersonId == person));
            receiptRecoveryPhase = canEdit ? ReceiptRecoveryPhase.None : ReceiptRecoveryPhase.Forbidden;
            RefreshReceiptEditorChoices();
            foreach (var row in ReceiptCategories.Items.OfType<ReceiptCategoryRow>())
                row.SetName(categories.Categories.FirstOrDefault(c => c.CategoryId == row.CategoryId)?.Name ?? "科目名未取得");
            receiptDetailRendering = true;
            try { RenderReceiptDetailPeople(); RefreshReceiptTaxChoices(); }
            finally { receiptDetailRendering = false; }
            RefreshReceiptCalculation();
            receiptAuthentication = null;
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        { if (Current()) NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。"); }
        catch (Exception error) when (error is LedgerRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            PeopleRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            PaymentMethodRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            CategoryRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound } or
            TaxRateRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        { if (Current()) { receiptRecoveryPhase = ReceiptRecoveryPhase.Unavailable; receiptAuthentication = null; } }
        catch (Exception error) when (error is LedgerRequestException or PeopleRequestException or PaymentMethodRequestException or
            CategoryRequestException or TaxRateRequestException or ApiConnectionException or HttpRequestException or IOException or OperationCanceledException)
        { if (Current()) receiptRecoveryPhase = ReceiptRecoveryPhase.Failed; }
        finally
        {
            if (ReferenceEquals(receiptRecoveryLoading, loading)) receiptRecoveryLoading = null;
            if (!closed && ReferenceEquals(editor, receiptEditor) && receiptRecoveryPhase != ReceiptRecoveryPhase.Login)
            { RenderReceiptRecovery(); RestoreReceiptRecoveryView(context, editor); }
        }
    }

    private void RenderReceiptRecovery()
    {
        var readOnly = ReceiptRecoveryReadOnly;
        foreach (var text in ReceiptTextInputs) text.IsReadOnly = readOnly;
        foreach (var choice in new Control[] { ReceiptEditorPayment, ReceiptUnit, ReceiptCategoryChoice, ReceiptTaxIncluded, ReceiptTaxExcluded,
            ReceiptTaxFloor, ReceiptTaxHalfUp, ReceiptItemTax, ReceiptItemExpense, ReceiptItemOccurrence }) choice.IsEnabled = !readOnly;
        foreach (var box in ReceiptDetailPeople.Children.OfType<CheckBox>()) box.IsEnabled = !readOnly;
        UpdateReceiptCategoryActions();
        ReceiptCategoryRemove.IsEnabled = !readOnly && receiptEditor?.SelectedGroup is not null;
        if (readOnly)
        {
            foreach (var button in new[] { ReceiptDetailApply, ReceiptDetailReuse, ReceiptDetailDelete, ReceiptDetailUp, ReceiptDetailDown }) button.IsEnabled = false;
            RefreshReceiptCalculation();
        }
        else if (receiptEditor is not null) RenderReceiptDetailList(refreshRows: false);
        ReceiptRecoveryNotice.Message = receiptRecoveryPhase switch
        {
            ReceiptRecoveryPhase.Login => "入力を保持しています。再ログインしてください。",
            ReceiptRecoveryPhase.Loading => "マスタ情報を読み込み中…",
            ReceiptRecoveryPhase.Failed => "マスタ情報を取得できませんでした",
            ReceiptRecoveryPhase.Forbidden => "編集権限がなくなったため、保存できません",
            ReceiptRecoveryPhase.Unavailable => "レシートが削除されたか、アクセスできなくなりました。",
            _ => ""
        };
        ReceiptRecoveryNotice.IsOpen = readOnly;
        ReceiptMasterRetry.Visibility = receiptRecoveryPhase == ReceiptRecoveryPhase.Failed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestoreReceiptRecoveryView(ReceiptAuthentication context, Monetail.Windows.Client.Receipts.ReceiptEditorState editor)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (closed || receiptRecoveryPhase == ReceiptRecoveryPhase.Login || !ReferenceEquals(editor, receiptEditor) || HomePage.Visibility != Visibility.Visible ||
                account?.AuthUserId != context.AccountId || connection.Endpoint?.Value != context.ApiOrigin || currentLedger?.LedgerId != context.LedgerId) return;
            ReceiptInputScroller.UpdateLayout();
            if (context.Focus is { IsLoaded: true, IsEnabled: true, Visibility: Visibility.Visible } focus)
            {
                focus.Focus(FocusState.Programmatic);
                if (focus is TextBox text) text.Select(context.SelectionStart, context.SelectionLength);
            }
            foreach (var (viewer, x, y) in context.Scrolls) if (viewer.IsLoaded) viewer.ChangeView(x, y, null, true);
            ReceiptDate.SuppressCompletion = ReceiptTime.SuppressCompletion = false;
        });
    }
}
