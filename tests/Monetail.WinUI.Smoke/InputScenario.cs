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
        // 連続Focus呼出しで前のLostFocusを追い越さず、補完イベントの完了を観測する。
        async Task FocusAndObserve(Control target)
        {
            await Wait(() => target.IsLoaded && target.ActualWidth > 0);
            var previous = FocusManager.GetFocusedElement(panel.XamlRoot) as FrameworkElement;
            if (ReferenceEquals(previous, target)) return;
            var lost = previous is null;
            RoutedEventHandler observed = (_, _) => lost = true;
            if (previous is not null) previous.LostFocus += observed;
            try
            {
                if (!target.Focus(FocusState.Keyboard)) throw new InvalidOperationException("入力試験のフォーカス移動に失敗しました。");
                await Wait(() => lost && ReferenceEquals(FocusManager.GetFocusedElement(panel.XamlRoot), target));
            }
            finally { if (previous is not null) previous.LostFocus -= observed; }
        }
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

            await FocusAndObserve(date);
            date.Text = "not a date";
            await FocusAndObserve(cancel);
            await Settle();
            Check(date.Text == "not a date", "キャンセルへ移るとき日付を補完しない");
            await FocusAndObserve(date);
            await FocusAndObserve(save);
            await Settle();
            Check(date.Text == "", "空欄可の日付は不正入力を空欄に補正");
            date.AllowEmpty = false;
            await FocusAndObserve(date);
            date.Text = "不正";
            var before = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            await FocusAndObserve(save);
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

            var dialogName = new TextBox { Header = "人物名" };
            var dialogDate = new DateInputBox { Header = "有効終了日" };
            var dialogBody = new StackPanel { Spacing = 12 };
            dialogBody.Children.Add(dialogName);
            dialogBody.Children.Add(dialogDate);
            var dialog = new ContentDialog { Title = "人物の追加", Content = dialogBody,
                PrimaryButtonText = "登録", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.None,
                XamlRoot = panel.XamlRoot };
            var dialogSaves = 0;
            var dialogKeyboard = FormKeyboard.ForDialog(dialog, () => { dialogSaves++; return Task.CompletedTask; }, dialogName, dialogDate);
            var showing = dialog.ShowAsync();
            try
            {
                await Wait(() => dialog.IsLoaded && dialogDate.ActualWidth > 0);
                var primary = All(dialog).OfType<Button>().Single(button => Equals(button.Content, dialog.PrimaryButtonText));
                dialogDate.Focus(FocusState.Keyboard);
                dialogDate.Text = "20260401";
                await dialogKeyboard.HandleEnter(false, false)!;
                await Settle();
                Check(ReferenceEquals(FocusManager.GetFocusedElement(panel.XamlRoot), primary) && dialogSaves == 0 &&
                    dialogDate.Text == "2026/04/01", "ダイアログの最終欄のEnterも保存せず標準の登録ボタンへ移動");
                dialogName.Focus(FocusState.Keyboard);
                await dialogKeyboard.HandleEnter(true, false)!;
                Check(dialogSaves == 1, "ダイアログ内でもCtrl+Enterの保存を1回だけ呼ぶ");
                dialog.IsPrimaryButtonEnabled = false;
                Check(dialogKeyboard.HandleEnter(true, false) is null && dialogSaves == 1,
                    "ダイアログの保存不可状態はCtrl+Enterでも尊重する");
            }
            finally { dialog.Hide(); await showing; }
        }
        finally { inputWindow.Close(); }
    }
}
