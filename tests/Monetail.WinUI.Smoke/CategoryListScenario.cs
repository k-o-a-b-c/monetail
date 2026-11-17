using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckCategoryListBoundary()
    {
        using var fixture = new SyntheticConnection(saved: true);
        var server = new CategoryServer();
        var root = await OpenCategoriesWindow(fixture, server);
        var view = CategoryListRoot(root);
        var names = new[] { "AddCategoryButton", "EditCategoryButton", "DeleteCategoryButton", "CategoryOrderButton", "IncludeInactiveCategories", "CategoriesList" };
        Check(names.Append("EmptyCategories").All(n => ReferenceEquals(view.FindName(n), Find<FrameworkElement>(view, n))) &&
            names.All(n => AutomationProperties.GetAutomationId(Find<FrameworkElement>(view, n)) == n) && !view.IsTabStop,
            "科目一覧内部のNameScopeと既存AutomationIdを維持し、ViewのTab停止は追加しない");
        SelectCategoryRow(root, server.Rows[0].CategoryId);
        await FocusTransferControl(Find<Button>(view, names[0]));
        foreach (var name in names.Skip(1))
        {
            await PressReceiptKey(0x09);
            await Wait(() => TransferControlHasFocus(root, Find<Control>(view, name)));
        }
        foreach (var name in names.Take(names.Length - 1).Reverse())
        {
            await PressReceiptKey(0x09, 0x10);
            await Wait(() => TransferControlHasFocus(root, Find<Control>(view, name)));
        }
        Check(server.Reads == 1 && server.Writes.Count == 0, "科目一覧のTab/Shift+Tabは追加→編集→削除→並び順→期間外表示→一覧の順で移動し、通信しない");
        await FocusTransferControl(Find<CheckBox>(view, "IncludeInactiveCategories"));
        await PressReceiptKey(0x20);
        await Wait(() => view.IncludeInactive && Find<ListView>(view, "CategoriesList").Items.Count == server.Rows.Count);
        await PressReceiptKey(0x20);
        await Wait(() => !view.IncludeInactive && Find<ListView>(view, "CategoriesList").Items.Count == 2);
        Check(view.SelectedCategory?.CategoryId == server.Rows[0].CategoryId && server.Reads == 1 && server.Writes.Count == 0,
            "ViewのSpace操作による期間フィルターも選択を保持し追加GETや書込を行わない");
    }
}
