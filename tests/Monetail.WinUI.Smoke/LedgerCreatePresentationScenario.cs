using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.WinUI.Views.Ledgers;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckCreationPresentation(FrameworkElement root, LedgerCreatePage page, TextBox name, Button create, Button refresh)
    {
        var error = Find<TextBlock>(root, "LedgerNameError");
        var result = Find<TextBlock>(root, "MembershipResult");
        Check(Find<TextBlock>(root, "PageTitle").Text == "家計簿がありません" &&
            Find<TextBlock>(root, "NoLedgersNotice").Text == "現在、参加している家計簿はありません。" &&
            Find<TextBlock>(root, "CreateSectionTitle").Text == "新しく家計簿を作成" &&
            Find<TextBlock>(root, "InvitationHeading").Text == "招待された家計簿に参加" &&
            Find<TextBlock>(root, "InvitationDescription").Text == "管理者から招待リンクを受け取り、ブラウザーで参加手続きを完了してください。" &&
            Find<TextBlock>(root, "ConfirmationHint").Text == "参加後は、以下のボタンを押してください。" &&
            Equals(refresh.Content, "参加中の家計簿を確認"),
            "所属0件は作成・招待参加を分け、確定したタイトル・説明・確認ボタンを表示する");
        Check(error.Visibility == Visibility.Collapsed && result.Visibility == Visibility.Collapsed &&
            double.IsNaN(error.Height) && error.ReadLocalValue(FrameworkElement.MinHeightProperty) == DependencyProperty.UnsetValue &&
            !Find<InfoBar>(root, "ErrorNotice").IsOpen && !All(page).OfType<InfoBar>().Any(),
            "初期表示は欄下エラー・所属確認結果を隠し、エラー用固定高さやPage内InfoBarを作らない");
        name.Text = " \u3000 ";
        await InvokeAsync(() => create);
        await Wait(() => error.Visibility == Visibility.Visible);
        Check(error.Text == "家計簿名は1〜100文字で入力してください。" && !Find<InfoBar>(root, "ErrorNotice").IsOpen &&
            Equals(TransferRuntime("busy"), false) && !Find<ProgressRing>(root, "BusyProgress").IsActive &&
            ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name) && AutomationProperties.GetHelpText(name) == error.Text,
            "未入力の検証は欄直下のテキストとヘルプを表示し、フォーカスを戻しAPI処理中/InfoBarへ進まない");
        name.Text = new string('a', 101);
        await InvokeAsync(() => create);
        Check(error.Visibility == Visibility.Visible && name.Text.Length == 101 && !Find<InfoBar>(root, "ErrorNotice").IsOpen,
            "上限超過の不正な生入力は切り詰めず、入力欄直下のエラーを維持する");
        name.Text = new string('a', 100);
        await Wait(() => error.Visibility == Visibility.Collapsed);
        Check(error.Text.Length == 0 && AutomationProperties.GetHelpText(name).Length == 0 && TransferRuntime("creationDraft") is null,
            "有効な100文字へ修正すると共有検証でエラーを解除し、再送draft/requestIdを作成しない");
        var keyboard = (FormKeyboard)(typeof(LedgerCreatePage).GetField("keyboard", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page)
            ?? throw new InvalidOperationException("Pageの共通キーボードがありません。"));
        var composing = (HashSet<TextBox>)(typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(keyboard)
            ?? throw new InvalidOperationException("共通IME状態がありません。"));
        await ActivateReceiptInputWindowAsync();
        await FocusTransferControl(name);
        composing.Add(name);
        try
        {
            Check(keyboard.HandleEnter(false, false) is null && keyboard.HandleEnter(true, false) is null &&
                ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name) && TransferRuntime("creationDraft") is null,
                "Pageの共通IME合成ガード中はEnterで移動せずCtrl+Enterでも作成を通知しない");
        }
        finally
        {
            composing.Clear();
        }
        name.Text = new string('a', 101);
        await InvokeAsync(() => create);
        await Wait(() => error.ActualHeight > 0);
        var active = window ?? throw new InvalidOperationException("Windowがありません。");
        var size = active.AppWindow.Size;
        var text = new[] { "PageTitle", "NoLedgersNotice", "CreateSectionTitle", "InvitationHeading", "InvitationDescription", "ConfirmationHint", "LedgerNameError", "MembershipResult" }
            .Select(id => Find<TextBlock>(root, id)).Select(element => (Element: element, Size: element.FontSize)).ToArray();
        var controls = new Control[] { name, create, refresh, Find<Button>(root, "CreatePageLogoutButton") }
            .Select(element => (Element: element, Size: element.FontSize)).ToArray();
        try
        {
            foreach (var item in text)
                item.Element.FontSize = item.Size * 2;
            foreach (var item in controls)
                item.Element.FontSize = item.Size * 2;
            var scale = root.XamlRoot.RasterizationScale;
            active.AppWindow.ResizeClient(new SizeInt32((int)(520 * scale), (int)(620 * scale)));
            var scroll = All(page).OfType<ScrollViewer>().Single(view => view.Content is StackPanel);
            await Wait(() => page.ActualWidth < 600 && scroll.ScrollableHeight > 0);
            Check(text.Where(item => item.Element.Visibility == Visibility.Visible).All(item => item.Element.TextWrapping == TextWrapping.Wrap &&
                item.Element.ActualWidth <= page.ActualWidth - 64 + 1) && scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled &&
                refresh.ActualWidth <= page.ActualWidth - 64 + 1 && error.ActualHeight > 0,
                "狭幅と2倍文字の模擬表示でも説明/欄下エラーは折返し、固定高さで隠さず縦スクロールできる");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(name)
                ?? throw new InvalidOperationException("入力欄のアクセシビリティ情報がありません。");
            Check(peer.GetName() == "家計簿名" && peer.GetHelpText() == error.Text &&
                AutomationProperties.GetLiveSetting(error) == AutomationLiveSetting.Polite &&
                AutomationProperties.GetLiveSetting(result) == AutomationLiveSetting.Polite &&
                result.Text == "参加中の家計簿は見つかりませんでした。",
                "入力ラベル・エラーヘルプと通常の所属確認結果をアクセシビリティ通知へ公開する");
        }
        finally
        {
            foreach (var item in text)
                item.Element.FontSize = item.Size;
            foreach (var item in controls)
                item.Element.FontSize = item.Size;
            active.AppWindow.Resize(size);
        }
        name.Text = "修正";
        await Wait(() => error.Visibility == Visibility.Collapsed);
        name.Text = "";
    }
}
