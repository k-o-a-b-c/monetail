using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private CategoriesInfo? categoryInfo;
    private PeoplePermissions? categoryPermissions;
    private CategoryInfo? editingCategory, latestCategory;
    private CategoryCreationDraft? categoryDraft;
    private readonly HashSet<Guid> missingCategories = [];
    private bool categoryDialogOpen, categoryDialogClosing, forceCategoryClose, deletingCategory, categoryEditorDenied;
    private Task categoryDialogTask = Task.CompletedTask;
    private FormKeyboard categoryKeyboard = null!;
    private CategoriesWorkspace Categories => new(connection.Api, connection.Session, currentLedger!.LedgerId);
    private bool CanViewCategories => categoryPermissions is null || categoryPermissions.CurrentRole is not null;
    private bool CanManageCategories => account is not null && categoryInfo is not null && categoryPermissions?.CurrentRole == "Admin";
    private bool CanChangeCategory(CategoryInfo row) => !missingCategories.Contains(row.CategoryId) && CanManageCategories;
    private string SelectedCategoryIncome => (CategoryIncome.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private string SelectedCategoryTax => (CategoryTax.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void InitializeCategories()
    {
        WorkspaceRoot.Children.Remove(CategoryDialog);
        CategoryFrom.IsCancellationTarget = CategoryTo.IsCancellationTarget = focused => focused is Button b && Equals(b.Content, CategoryDialog.CloseButtonText);
        categoryKeyboard = FormKeyboard.ForDialog(CategoryDialog, SaveCategoryAsync, CategoryName, CategoryFrom, CategoryTo);
        _ = new FormKeyboard(CategoryOrderContent, SaveCategoryOrderButton, SaveCategoryOrderAsync);
    }
    private void ResetCategories()
    {
        DiscardCategoryEditor(); DiscardCategoryOrder();
        categoryInfo = null; categoryPermissions = null; missingCategories.Clear();
        CategoriesList.ItemsSource = null; IncludeInactiveCategories.IsChecked = false; UpdateCategoryActions();
    }
    private Task LoadCategoriesAsync() => account is null || currentLedger is null ? Task.CompletedTask : RunAsync(async () =>
    {
        DisplayCategories(await Categories.ListAsync(lifetime.Token));
    }, LoadCategoriesAsync);
    private void DisplayCategories(CategoriesInfo info)
    {
        if (currentLedger is { } ledger) connection.Masters.UpdateCategories(ledger.LedgerId, info);
        categoryInfo = info; categoryPermissions = new(info.CurrentRole, null);
        missingCategories.Clear(); RenderCategories();
    }
    private void RenderCategories(Guid? selection = null)
    {
        if (categoryInfo is null) { UpdateCategoryActions(); return; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        selection ??= (CategoriesList.SelectedItem as CategoryRow)?.Category.CategoryId;
        var rows = categoryInfo.Categories.Where(r => IncludeInactiveCategories.IsChecked == true ||
            (r.ValidFrom is null || r.ValidFrom <= today) && (r.ValidTo is null || r.ValidTo >= today)).Select(r => new CategoryRow(r)).ToArray();
        CategoriesList.ItemsSource = rows;
        CategoriesList.SelectedItem = rows.FirstOrDefault(r => r.Category.CategoryId == selection);
        EmptyCategories.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCategoryActions();
    }
    private void FilterCategories(object sender, RoutedEventArgs args) => RenderCategories();
    private void SelectCategory(object sender, SelectionChangedEventArgs args) => UpdateCategoryActions();
    private void UpdateCategoryActions()
    {
        if (AddCategoryButton is null || CategoryDialog is null || SaveCategoryOrderButton is null) return;
        AddCategoryButton.IsEnabled = !busy && CanManageCategories;
        EditCategoryButton.IsEnabled = DeleteCategoryButton.IsEnabled = !busy && CategoriesList.SelectedItem is CategoryRow row && CanChangeCategory(row.Category);
        CategoryOrderButton.IsEnabled = !busy && CanManageCategories;
        CategoriesNavigation.IsEnabled = CanViewCategories;
        CategoryDialog.IsPrimaryButtonEnabled = !busy && !categoryEditorDenied && latestCategory is null &&
            (editingCategory is { } edit ? CanChangeCategory(edit) : CanManageCategories);
        UpdateCategoryOrderActions();
    }
    private void AddCategory(object sender, RoutedEventArgs args) { if (!busy && CanManageCategories) OpenCategoryEditor(null); }
    private void EditCategory(object sender, RoutedEventArgs args)
    { if (!busy && CategoriesList.SelectedItem is CategoryRow row && CanChangeCategory(row.Category)) OpenCategoryEditor(row.Category); }
    private void DeleteCategory(object sender, RoutedEventArgs args)
    { if (!busy && CategoriesList.SelectedItem is CategoryRow row && CanChangeCategory(row.Category)) OpenCategoryEditor(row.Category, true); }
    private void OpenCategoryEditor(CategoryInfo? row, bool deletion = false)
    {
        if (ManagementDialogOpen || orderedCategories is not null) return;
        editingCategory = row; latestCategory = null; categoryDraft = null; deletingCategory = deletion; categoryEditorDenied = false;
        CategoryDialog.Title = deletion ? "科目の削除" : row is null ? "科目の追加" : "科目の編集";
        CategoryDialog.PrimaryButtonText = deletion ? "削除" : row is null ? "登録" : "保存";
        CategoryDialog.DefaultButton = deletion ? ContentDialogButton.Close : ContentDialogButton.None;
        CategoryFields.Visibility = deletion ? Visibility.Collapsed : Visibility.Visible;
        CategoryDeleteNotice.Visibility = deletion ? Visibility.Visible : Visibility.Collapsed;
        FillCategoryFields(row);
        CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = false;
        CategoryConflict.Visibility = CategoryRestoredNotice.Visibility = Visibility.Collapsed;
        CategoryEditorContent.Visibility = Visibility.Visible;
        ClearCategoryErrors(); UpdateCategoryDeleteNotice(); UpdateCategoryActions();
        categoryDialogTask = ShowCategoryDialogAsync();
    }
    private void FillCategoryFields(CategoryInfo? row)
    {
        CategoryName.Text = row?.Name ?? "";
        CategoryIncome.SelectedItem = CategoryIncome.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, row?.IncomeExpense ?? "Expense"));
        CategoryTax.SelectedItem = CategoryTax.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, row?.TaxCategoryCode ?? "Standard"));
        CategoryFrom.Text = DateTimeInput.FormatDate(row?.ValidFrom); CategoryTo.Text = DateTimeInput.FormatDate(row?.ValidTo);
    }
    private async Task ShowCategoryDialogAsync()
    {
        categoryDialogOpen = true; categoryDialogClosing = forceCategoryClose = false; CategoryDialog.XamlRoot = WorkspaceRoot.XamlRoot;
        try { await CategoryDialog.ShowAsync(); }
        finally
        {
            categoryDialogOpen = categoryDialogClosing = false;
            if (!closed) { ClearCategoryEditor(); if (!forceCategoryClose && !CanViewCategories) ShowPage(HomePage); }
        }
    }
    private void CategoryDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (!deletingCategory) CategoryName.Focus(FocusState.Programmatic);
    }
    private void CategoryDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (busy && !forceCategoryClose && !closed) { args.Cancel = true; return; }
        categoryDialogClosing = true; CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = true;
    }
    private async void SaveCategory(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; var deferral = args.GetDeferral();
        try { args.Cancel = !await TrySaveCategoryAsync(); } finally { deferral.Complete(); }
    }
    private async Task SaveCategoryAsync() { if (!deletingCategory && await TrySaveCategoryAsync()) CategoryDialog.Hide(); }
    private async Task<bool> TrySaveCategoryAsync()
    {
        if (busy || categoryKeyboard.IsComposing || !CategoryDialog.IsPrimaryButtonEnabled) return false;
        ClearCategoryErrors(); CategoryInput? input = null;
        if (!deletingCategory)
        {
            try { input = new(CategoryName.Text, SelectedCategoryIncome, SelectedCategoryTax, CategoryFrom.Complete(), CategoryTo.Complete()); }
            catch (CategoryInputException error) { ShowCategoryError(error.Field, error.Message); return false; }
        }
        if (editingCategory is null && (categoryDraft is null || categoryDraft.Input != input))
            categoryDraft = new(input!);
        var saved = false;
        await RunAsync(async () =>
        {
            try
            {
                var result = deletingCategory ? await Categories.DeleteAsync(editingCategory!, lifetime.Token) : editingCategory is null
                    ? (await Categories.CreateAsync(categoryDraft!, lifetime.Token)).State
                    : await Categories.UpdateAsync(editingCategory, new(input!.Name, input.IncomeExpense, input.TaxCategoryCode, new(input.ValidFrom), new(input.ValidTo)), lifetime.Token);
                DisplayCategories(result); saved = true;
            }
            catch (CategoryRequestException error) { HandleCategoryFailure(error); }
        }, SaveCategoryAsync);
        return saved;
    }
    private void HandleCategoryFailure(CategoryRequestException error)
    {
        var conflictRow = error.LatestCategory ?? error.LatestCategories?.Categories.FirstOrDefault(r => r.CategoryId == editingCategory?.CategoryId);
        if (error.Code == "revision_conflict" && conflictRow is { } latest)
        {
            latestCategory = latest;
            LatestCategoryName.Text = latest.Name;
            LatestCategoryIncome.Text = CategoryRow.IncomeLabel(latest.IncomeExpense);
            LatestCategoryTax.Text = CategoryRow.TaxLabel(latest.TaxCategoryCode);
            LatestCategoryFrom.Text = DateTimeInput.FormatDate(latest.ValidFrom); LatestCategoryTo.Text = DateTimeInput.FormatDate(latest.ValidTo);
            CategoryConflict.Visibility = Visibility.Visible;
        }
        else
        {
            if (error.LatestCategories is { } collection) DisplayCategories(collection);
            ApplyCategoryFailureContext(error);
            if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) categoryEditorDenied = true;
            ShowCategoryError(error.Field, error.StatusCode switch
            {
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => "対象は削除されているか、この家計簿を操作できません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "category_referenced" => "参照が残っているため削除できません。使用を終了する場合は有効終了日を設定してください。",
                    "duplicate_category_name" => "同じ名前の科目が登録されています。",
                    "created_result_deleted" => "作成した科目は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして追加し直してください。",
                    "revision_conflict" => "ほかの操作で変更されています。入力を確認して再試行してください。",
                    _ => "科目の操作を完了できませんでした。入力内容を確認してください。"
                }
            });
        }
        UpdateCategoryActions();
    }
    private void ApplyCategoryFailureContext(CategoryRequestException error)
    {
        if (error.Permissions is { } permission) categoryPermissions = permission;
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) categoryPermissions = new(null, null);
        if (error.StatusCode == HttpStatusCode.NotFound && editingCategory is { } row) missingCategories.Add(row.CategoryId);
        RenderCategories();
    }
    private void UseLatestCategory(object sender, RoutedEventArgs args)
    {
        if (busy || latestCategory is null) return;
        editingCategory = latestCategory; latestCategory = null; FillCategoryFields(editingCategory); UpdateCategoryDeleteNotice();
        CategoryConflict.Visibility = Visibility.Collapsed; ClearCategoryErrors(); UpdateCategoryActions();
    }
    private void UpdateCategoryDeleteNotice() => CategoryDeleteNotice.Text = $"「{editingCategory?.Name}」を削除しますか。参照がある科目は削除できません。";
    private void DiscardCategoryEditor()
    {
        forceCategoryClose = true; if (categoryDialogOpen) CategoryDialog.Hide(); ClearCategoryEditor();
    }
    private void ClearCategoryEditor()
    {
        CategoryFrom.SuppressCompletion = CategoryTo.SuppressCompletion = true;
        CategoryEditorContent.Visibility = Visibility.Collapsed; editingCategory = latestCategory = null; categoryDraft = null;
        deletingCategory = categoryEditorDenied = false; ClearCategoryErrors(); UpdateCategoryActions();
    }
    private void ClearCategoryErrors() => CategoryNameError.Visibility = CategoryPeriodError.Visibility = CategoryChoiceError.Visibility = CategorySaveError.Visibility = Visibility.Collapsed;
    private void ShowCategoryError(string? field, string message)
    {
        var target = field == "name" ? CategoryNameError : field is "validFrom" or "validTo" ? CategoryPeriodError : field is "incomeExpense" or "taxCategoryCode" ? CategoryChoiceError : CategorySaveError;
        target.Text = message; target.Visibility = Visibility.Visible;
        if (field is not null) DispatcherQueue.TryEnqueue(() =>
        {
            if (!closed && categoryDialogOpen && !deletingCategory)
                (field == "name" ? (Control)CategoryName : field == "incomeExpense" ? CategoryIncome : field == "taxCategoryCode" ? CategoryTax : field == "validFrom" ? CategoryFrom : CategoryTo).Focus(FocusState.Programmatic);
        });
    }
}
