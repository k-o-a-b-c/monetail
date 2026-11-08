using System.Net;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private CategoryEditSnapshot? pendingCategoryEdit;
    private sealed record CategoryEditSnapshot(Guid AccountId, string ApiOrigin, LedgerInfo Ledger,
        CategoriesInfo Categories, CategoryInfo? Row, CategoryCreationDraft? Draft,
        string Name, string Income, string Tax, string From, string To, bool IncludeInactive, Guid OrderRevision, CategoryInfo[]? Order);
    private void RememberCategoryEdit()
    {
        if (account is null || currentLedger is null || categoryInfo is null ||
            (orderedCategories is null && (!categoryDialogOpen || deletingCategory))) return;
        pendingCategoryEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, categoryInfo,
            editingCategory, categoryDraft, CategoryName.Text, SelectedCategoryIncome, SelectedCategoryTax, CategoryFrom.Text, CategoryTo.Text,
            IncludeInactiveCategories.IsChecked == true, categoryOrderRevision, orderedCategories?.ToArray());
    }
    private void DropPendingCategoryEdit()
    {
        if (pendingCategoryEdit is null) return;
        pendingCategoryEdit = null; CategoryName.Text = CategoryFrom.Text = CategoryTo.Text = "";
        CategoryIncome.SelectedItem = CategoryTax.SelectedItem = null;
    }
    private async Task<bool> ResumeCategoryEditAsync(CancellationToken token)
    {
        if (pendingCategoryEdit is not { } draft || account is null) return false;
        if (draft.AccountId != account.AuthUserId || draft.ApiOrigin != connection.Endpoint!.Value) { DropPendingCategoryEdit(); return false; }
        await categoryDialogTask;
        var ledger = draft.Ledger; var categories = draft.Categories;
        PeoplePermissions? denied = null; string? failure = null;
        try
        {
            ledger = await connection.Ledgers.OpenAsync(draft.Ledger.LedgerId, token);
            categories = await new CategoriesWorkspace(connection.Api, connection.Session, ledger.LedgerId).ListAsync(token);
        }
        catch (LedgerRequestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { denied = new(null, null); failure = "この家計簿を開けません。未保存の入力を残していますが保存できません。"; }
        catch (CategoryRequestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { denied = e.Permissions ?? new(null, null); failure = "現在の権限では操作できません。未保存の入力は残しています。"; }
        DisplayLedger(ledger); DisplayCategories(categories);
        if (denied is not null) categoryPermissions = denied;
        ShowPage(ManagementPage); ManagementNavigation.SelectedItem = CategoriesNavigation;
        PaymentsContent.Visibility = PeopleContent.Visibility = ParticipantsContent.Visibility = InvitationsContent.Visibility = AccountContent.Visibility = ConnectionSettingsContent.Visibility = Visibility.Collapsed;
        CategoriesContent.Visibility = Visibility.Visible; IncludeInactiveCategories.IsChecked = draft.IncludeInactive;
        if (draft.Order is { } order)
        {
            var current = categories.Categories.ToDictionary(r => r.CategoryId);
            BeginCategoryOrder(draft.OrderRevision, order.Select(r => current.GetValueOrDefault(r.CategoryId) ?? r));
            categoryOrderDenied = failure is not null || !CanManageCategories;
            ShowCategoryOrderNotice(failure ?? (categoryOrderDenied ? "現在の権限では保存できません。未保存の並び順は残しています。" : "未保存の並び順を復元しました。内容を確認してください。"), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        }
        else
        {
            OpenCategoryEditor(draft.Row);
            CategoryIncome.SelectedItem = CategoryIncome.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>().Single(i => Equals(i.Tag, draft.Income));
            CategoryTax.SelectedItem = CategoryTax.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>().Single(i => Equals(i.Tag, draft.Tax));
            CategoryName.Text = draft.Name; CategoryFrom.Text = draft.From; CategoryTo.Text = draft.To; categoryDraft = draft.Draft;
            if (draft.Row is not null && !categories.Categories.Any(r => r.CategoryId == draft.Row.CategoryId)) missingCategories.Add(draft.Row.CategoryId);
            categoryEditorDenied = failure is not null || (draft.Row is null ? !CanManageCategories : !CanChangeCategory(draft.Row));
            CategoryRestoredNotice.Visibility = Visibility.Visible;
            if (categoryEditorDenied) ShowCategoryError(null, failure ?? "現在の権限では保存できません。未保存の入力は残しています。");
        }
        pendingCategoryEdit = null; RenderCategories(); UpdateCategoryActions(); return true;
    }
}
