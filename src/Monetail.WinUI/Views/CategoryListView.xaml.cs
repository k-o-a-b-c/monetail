using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Views;

/// <summary>表示・期間フィルター・選択を所有し、権限や操作の実行は所有しない。</summary>
public sealed partial class CategoryListView : UserControl
{
    private IReadOnlyList<CategoryInfo>? categories;
    public CategoryListView() => InitializeComponent();

    public IReadOnlyList<CategoryInfo>? Categories
    {
        get => categories;
        set
        {
            categories = value;
            if (value is null) CategoriesList.ItemsSource = null;
            else RenderCategories();
        }
    }
    public CategoryInfo? SelectedCategory
    {
        get => (CategoriesList.SelectedItem as CategoryRow)?.Category;
        set => CategoriesList.SelectedItem = CategoriesList.Items.OfType<CategoryRow>().FirstOrDefault(r => r.Category.CategoryId == value?.CategoryId);
    }
    public bool IncludeInactive
    {
        get => IncludeInactiveCategories.IsChecked == true;
        set => IncludeInactiveCategories.IsChecked = value;
    }
    public void SetActionsEnabled(bool manage, bool changeSelected)
    {
        AddCategoryButton.IsEnabled = CategoryOrderButton.IsEnabled = manage;
        EditCategoryButton.IsEnabled = DeleteCategoryButton.IsEnabled = changeSelected;
    }

    public event SelectionChangedEventHandler? SelectionChanged;
    public event RoutedEventHandler? AddRequested;
    public event RoutedEventHandler? EditRequested;
    public event RoutedEventHandler? DeleteRequested;
    public event RoutedEventHandler? OrderRequested;

    private void RenderCategories()
    {
        if (categories is null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var selection = SelectedCategory?.CategoryId;
        var rows = categories.Where(r => IncludeInactive ||
            (r.ValidFrom is null || r.ValidFrom <= today) && (r.ValidTo is null || r.ValidTo >= today)).Select(r => new CategoryRow(r)).ToArray();
        CategoriesList.ItemsSource = rows;
        CategoriesList.SelectedItem = rows.FirstOrDefault(r => r.Category.CategoryId == selection);
        EmptyCategories.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void FilterCategories(object sender, RoutedEventArgs args) => RenderCategories();
    private void SelectCategory(object sender, SelectionChangedEventArgs args) => SelectionChanged?.Invoke(this, args);
    private void AddCategory(object sender, RoutedEventArgs args) => AddRequested?.Invoke(this, args);
    private void EditCategory(object sender, RoutedEventArgs args) => EditRequested?.Invoke(this, args);
    private void DeleteCategory(object sender, RoutedEventArgs args) => DeleteRequested?.Invoke(this, args);
    private void OpenCategoryOrder(object sender, RoutedEventArgs args) => OrderRequested?.Invoke(this, args);
}
