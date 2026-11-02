using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckInputControls()
    {
        // 物理キーの注入とは分け、製品と同じハンドラーと実コントロールのフォーカスを検証する。
        var first = new TextBox { Header = "人物名" };
        var disabled = new TextBox { IsEnabled = false };
        var hidden = new TextBox { Visibility = Visibility.Collapsed };
        var date = new DateInputBox { Header = "日付", AllowEmpty = true };
        var save = new Button { Content = "保存" };
        var cancel = new Button { Content = "キャンセル" };
        var multiline = new TextBox { AcceptsReturn = true };
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var panel = new StackPanel { Padding = new Thickness(24), Spacing = 8 };
        foreach (var control in new UIElement[] { first, disabled, hidden, date, save, cancel, multiline, combo })
            panel.Children.Add(control);
        var saves = 0;
        var navigation = new FormKeyboard(panel, save, () => { saves++; return Task.CompletedTask; }, first, date, multiline);
        date.CancellationTarget = cancel;
        var inputWindow = new Window { Content = panel };
        inputWindow.Activate();
        try
        {
            await Wait(() => panel.IsLoaded);
            first.Focus(FocusState.Keyboard);
            await Settle();
            await navigation.HandleEnter(false, false)!;
            Check(ReferenceEquals(FocusManager.GetFocusedElement(panel.XamlRoot), date), "EnterはTab順に従い無効・非表示の欄を飛ばす");
            date.Text = "２０２６０４０１";
            Check(date.Text == "２０２６０４０１", "日付は入力途中に補完しない");
            await navigation.HandleEnter(false, false)!;
            await Settle();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(panel.XamlRoot), save) && saves == 0 && date.Text == "2026/04/01",
                "最後の入力欄のEnterは日付を確定して保存ボタンへ移動し、そのキーでは保存しない");
            Check(navigation.HandleEnter(false, false) is null && saves == 0, "ボタン上のEnterを横取りしない");

            date.Focus(FocusState.Keyboard);
            await Settle();
            await navigation.HandleEnter(false, true)!;
            Check(ReferenceEquals(FocusManager.GetFocusedElement(panel.XamlRoot), first), "Shift+Enterは前の入力へ移動");
            await navigation.HandleEnter(true, false)!;
            Check(saves == 1, "Ctrl+Enterは登録・保存の処理を1回呼ぶ");
            save.IsEnabled = false;
            Check(navigation.HandleEnter(true, false) is null && saves == 1, "Ctrl+Enterは無効な保存を実行しない");
            save.IsEnabled = true;

            date.Focus(FocusState.Keyboard);
            date.Text = "not a date";
            cancel.Focus(FocusState.Keyboard);
            await Settle();
            Check(date.Text == "not a date", "キャンセルへ移るとき日付を補完しない");
            date.Focus(FocusState.Keyboard);
            save.Focus(FocusState.Keyboard);
            await Settle();
            Check(date.Text == "", "空欄可の日付は不正入力を空欄に補正");
            date.AllowEmpty = false;
            date.Focus(FocusState.Keyboard);
            date.Text = "不正";
            var before = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            save.Focus(FocusState.Keyboard);
            await Settle();
            Check(date.Text == before || date.Text == DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                "必須の日付は不正入力をWindowsの本日に補正");

            multiline.Focus(FocusState.Keyboard);
            await Settle();
            Check(navigation.HandleEnter(false, false) is null && navigation.HandleEnter(true, false) is null && saves == 1,
                "複数行入力の標準操作を優先する");
            combo.Focus(FocusState.Keyboard);
            combo.IsDropDownOpen = true;
            await Settle();
            Check(navigation.HandleEnter(false, false) is null && navigation.HandleEnter(true, false) is null && saves == 1,
                "候補を開いている間のEnterはコンボボックスを優先する");
            combo.IsDropDownOpen = false;
        }
        finally { inputWindow.Close(); }
    }
}
