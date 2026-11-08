using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private List<CategoryInfo>? orderedCategories;
    private Guid categoryOrderRevision;
    private bool categoryOrderDenied;
    private void OpenCategoryOrder(object sender, RoutedEventArgs args)
    {
        if (busy || ManagementDialogOpen || !CanManageCategories || categoryInfo is null) return;
        BeginCategoryOrder(categoryInfo.OrderRevision, categoryInfo.Categories);
    }
    private void BeginCategoryOrder(Guid revision, IEnumerable<CategoryInfo> rows)
    {
        categoryOrderRevision = revision; orderedCategories = rows.ToList(); categoryOrderDenied = false;
        CategoriesContent.Visibility = Visibility.Collapsed; CategoryOrderContent.Visibility = Visibility.Visible;
        CategoryOrderNotice.IsOpen = false; CategoryOrderNotice.Visibility = Visibility.Collapsed;
        RenderCategoryOrder(-1);
    }
    private void RenderCategoryOrder(int index)
    {
        CategoryOrderList.ItemsSource = orderedCategories?.Select(r => new CategoryRow(r)).ToArray(); CategoryOrderList.SelectedIndex = index;
        if (CategoryOrderList.SelectedItem is { } selected) CategoryOrderList.ScrollIntoView(selected);
        UpdateCategoryOrderActions();
    }
    private void UpdateCategoryOrderActions()
    {
        if (SaveCategoryOrderButton is null) return;
        var allowed = !busy && orderedCategories is not null && !categoryOrderDenied && CanManageCategories;
        SaveCategoryOrderButton.IsEnabled = allowed;
        MoveCategoryUpButton.IsEnabled = allowed && CategoryOrderList.SelectedIndex > 0;
        MoveCategoryDownButton.IsEnabled = allowed && CategoryOrderList.SelectedIndex >= 0 && CategoryOrderList.SelectedIndex < orderedCategories!.Count - 1;
        CancelCategoryOrderButton.IsEnabled = !busy;
    }
    private void SelectCategoryOrder(object sender, SelectionChangedEventArgs args) => UpdateCategoryOrderActions();
    private void MoveCategoryUp(object sender, RoutedEventArgs args) => MoveCategory(-1);
    private void MoveCategoryDown(object sender, RoutedEventArgs args) => MoveCategory(1);
    private void MoveCategory(int direction)
    {
        if (busy || categoryOrderDenied || orderedCategories is null || !CanManageCategories) return;
        var from = CategoryOrderList.SelectedIndex; var to = from + direction;
        if (from < 0 || to < 0 || to >= orderedCategories.Count) return;
        (orderedCategories[from], orderedCategories[to]) = (orderedCategories[to], orderedCategories[from]); RenderCategoryOrder(to);
    }
    private async void SaveCategoryOrder(object sender, RoutedEventArgs args) => await SaveCategoryOrderAsync();
    private async Task SaveCategoryOrderAsync()
    {
        if (busy || !SaveCategoryOrderButton.IsEnabled || orderedCategories is null) return;
        var revision = categoryOrderRevision; var ids = orderedCategories.Select(r => r.CategoryId).ToArray();
        CategoryOrderNotice.IsOpen = false; CategoryOrderNotice.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            try
            {
                DisplayCategories(await Categories.SaveOrderAsync(revision, ids, lifetime.Token));
                DiscardCategoryOrder(); CategoriesContent.Visibility = Visibility.Visible;
            }
            catch (CategoryRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestCategories is { } latest)
                {
                    DisplayCategories(latest); BeginCategoryOrder(latest.OrderRevision, latest.Categories);
                    ShowCategoryOrderNotice("ほかの操作で変更されています。未保存の並び順を破棄し、最新の内容に切り替えました。", InfoBarSeverity.Informational);
                }
                else
                {
                    ApplyCategoryFailureContext(error);
                    categoryOrderDenied = error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                    ShowCategoryOrderNotice(error.StatusCode switch {
                        HttpStatusCode.Forbidden => "並び順を保存する権限がありません。変更内容は保存されていません。",
                        HttpStatusCode.NotFound => "対象の家計簿を操作できません。変更内容は保存されていません。",
                        HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                        _ => "並び順を保存できませんでした。内容を確認してください。"
                    });
                }
                UpdateCategoryOrderActions();
            }
        }, SaveCategoryOrderAsync);
    }
    private void CancelCategoryOrder(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        DiscardCategoryOrder(); if (!CanViewCategories) ShowPage(HomePage); else CategoriesContent.Visibility = Visibility.Visible;
    }
    private void DiscardCategoryOrder()
    {
        orderedCategories = null; categoryOrderRevision = Guid.Empty; categoryOrderDenied = false;
        CategoryOrderList.ItemsSource = null; CategoryOrderContent.Visibility = CategoryOrderNotice.Visibility = Visibility.Collapsed;
        CategoryOrderNotice.IsOpen = false; UpdateCategoryOrderActions();
    }
    private void ShowCategoryOrderNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        CategoryOrderNotice.Message = message; CategoryOrderNotice.Severity = severity;
        CategoryOrderNotice.Visibility = Visibility.Visible; CategoryOrderNotice.IsOpen = true;
    }
}
