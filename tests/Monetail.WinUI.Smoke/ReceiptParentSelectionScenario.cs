using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.Windows.Client.Receipts;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private ReceiptParentInputState ParentInput => (ReceiptParentInputState)typeof(MainWindow).GetField("receiptParentInput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private async Task SelectReceiptRadio(FrameworkElement root, string name, bool keyboard = false)
    {
        await Wait(() => Find<RadioButton>(root, name) is { IsLoaded: true, ActualWidth: > 0, IsEnabled: true });
        var radio = Find<RadioButton>(root, name);
        if (keyboard)
        {
            if (!radio.Focus(FocusState.Keyboard)) throw new InvalidOperationException("RadioButtonへフォーカスできません。");
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), radio));
        }
        if (Environment.CurrentManagedThreadId != uiThreadId || radio.XamlRoot?.IsHostVisible != true) throw new InvalidOperationException("RadioButtonの操作準備ができていません。");
        var clicked = false; RoutedEventHandler observed = (_, _) => clicked = true; radio.Click += observed;
        try
        {
            if (keyboard) await PressReceiptRadioSpaceAsync(radio);
            else await ClickReceiptRadioAsync(radio);
            await Wait(() => clicked && radio.IsChecked == true);
        }
        finally { radio.Click -= observed; }
    }
    private async Task CheckReceiptParentSelection()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(null);
        await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        var editor = ReceiptEditing!; var state = ParentInput;
        Check(Find<RadioButton>(root, "ReceiptTaxIncluded").IsChecked == true && Find<RadioButton>(root, "ReceiptTaxFloor").IsChecked == true &&
            state.CanAutofillTaxInclusion && state.CanAutofillTaxRounding && !editor.NeedsLeaveConfirmation, "RadioButtonの新規初期選択は自動補完対象で未変更");
        var interval = Find<TextBox>(root, "ReceiptInterval"); var unit = Find<ComboBox>(root, "ReceiptUnit");
        Check(interval.Text == "" && interval.IsEnabled && unit.IsEnabled && ((ComboBoxItem)unit.SelectedItem).Tag.ToString() == "Month" &&
            editor.Header.RecurrenceInterval is null && editor.Header.RecurrenceUnit is null, "周期は常時入力可能・間隔空欄/表示月でも保存値は両方NULL");
        await SelectReceiptRadio(root, "ReceiptTaxIncluded");
        Check(state.TaxInclusionSource == ReceiptInputSource.Manual && state.CanAutofillTaxRounding && !editor.NeedsLeaveConfirmation, "選択済み税込の再クリックもその項目だけ手動、フラグだけでは未保存変更にしない");
        await SelectReceiptRadio(root, "ReceiptTaxFloor");
        Check(state.TaxRoundingSource == ReceiptInputSource.Manual && !editor.NeedsLeaveConfirmation, "選択済み切り捨ての再クリックも手動、値不変なら未変更");
        await SelectReceiptRadio(root, "ReceiptTaxExcluded", keyboard: true);
        await SelectReceiptRadio(root, "ReceiptTaxHalfUp", keyboard: true);
        Check(editor.Header.TaxInclusionMode == 1 && editor.Header.TaxRoundingMode == "half_up" && editor.NeedsLeaveConfirmation, "Spaceキーの標準選択でも値と手動設定を反映");
        await SelectReceiptRadio(root, "ReceiptTaxIncluded", keyboard: true); await SelectReceiptRadio(root, "ReceiptTaxFloor", keyboard: true);
        Check(!editor.NeedsLeaveConfirmation && !state.TryAutofillTaxInclusion(1) && !state.TryAutofillTaxRounding("half_up"), "値を初期値へ戻すと未保存変更は解消し手動保護は残る");
        window.RenderReceiptTaxValues(); Check(ReferenceEquals(state, ParentInput) && state.TaxInclusionSource == ReceiptInputSource.Manual, "再描画は手動設定状態を再作成・解除しない");
        unit.SelectedIndex = 2; await Task.Yield();
        Check(editor.Header.RecurrenceInterval is null && editor.Header.RecurrenceUnit is null && !editor.NeedsLeaveConfirmation, "間隔空欄時の表示単位変更は保存対象を変更しない");
        interval.Text = "2"; await Wait(() => editor.Header.RecurrenceInterval == 2 && editor.Header.RecurrenceUnit == "Year");
        Check(editor.NeedsLeaveConfirmation, "正の間隔と単位を入力すると周期を設定する");
        interval.Text = "2147483648"; await Wait(() => Find<TextBlock>(root, "ReceiptIntervalError").Visibility == Visibility.Visible);
        Check(interval.Text == "2147483648" && editor.Header.RecurrenceInterval == 2, "不正な周期はエラーと文字列を保持し周期なしへ変換しない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, false);
        Check(interval.Text == "2147483648" && ReferenceEquals(editor, ReceiptEditing), "不正な周期入力も離脱取消で保持する");
        interval.Text = ""; await Wait(() => editor.Header.RecurrenceInterval is null && editor.Header.RecurrenceUnit is null && Find<TextBlock>(root, "ReceiptIntervalError").Visibility == Visibility.Collapsed);
        Check(!editor.NeedsLeaveConfirmation, "間隔を空欄へ戻すと周期と未保存変更を解消する");
        interval.Text = "abc"; await Wait(() => Find<TextBlock>(root, "ReceiptIntervalError").Visibility == Visibility.Visible);
        Check(!editor.NeedsLeaveConfirmation && editor.Header.RecurrenceInterval is null && editor.Header.RecurrenceUnit is null, "周期なしモデルと不正な入力文字列を区別して保持する");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await ConfirmReceipt(root, false);
        Check(interval.Text == "abc" && ReferenceEquals(editor, ReceiptEditing), "モデル未変更でも不正な周期文字列は離脱保護の対象");
        interval.Text = ""; await Wait(() => Find<TextBlock>(root, "ReceiptIntervalError").Visibility == Visibility.Collapsed);
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await Wait(() => ReceiptEditing is null);
        await window.OpenReceiptEditorAsync(null); await Wait(() => Find<Button>(root, "ReceiptBack").ActualWidth > 0);
        var fresh = ParentInput; var secondEditor = ReceiptEditing!;
        Find<RadioButton>(root, "ReceiptTaxExcluded").IsChecked = true; Find<RadioButton>(root, "ReceiptTaxHalfUp").IsChecked = true;
        await Wait(() => secondEditor.Header.TaxInclusionMode == 1 && secondEditor.Header.TaxRoundingMode == "half_up");
        Check(fresh.TaxInclusionSource == ReceiptInputSource.Initial && fresh.TaxRoundingSource == ReceiptInputSource.Initial, "プログラムによるIsChecked設定は手動扱いにしない");
        Check(fresh.TryAutofillTaxInclusion(0) && fresh.TryAutofillTaxRounding("floor"), "未手動項目は表示値があっても自動補完できる");
        window.RenderReceiptTaxValues();
        Check(Find<RadioButton>(root, "ReceiptTaxIncluded").IsChecked == true && fresh.TaxInclusionSource == ReceiptInputSource.Automatic && !secondEditor.NeedsLeaveConfirmation,
            "自動値の再描画は手動扱いにせず元の入力値なら未変更");
        await SelectReceiptRadio(root, "ReceiptTaxIncluded", keyboard: true);
        Check(fresh.TaxInclusionSource == ReceiptInputSource.Manual && fresh.TaxRoundingSource == ReceiptInputSource.Automatic && !secondEditor.NeedsLeaveConfirmation,
            "Spaceで同値を選択してもその項目だけ自動値から手動保護へ移行する");
        await SelectReceiptRadio(root, "ReceiptTaxFloor", keyboard: true);
        Check(fresh.TaxRoundingSource == ReceiptInputSource.Manual && !secondEditor.NeedsLeaveConfirmation,
            "Spaceで同値を選択しても手動にし、値不変なら未保存変更は増やさない");
        await InvokeAsync(() => Find<Button>(root, "ReceiptBack")); await Wait(() => ReceiptEditing is null);
    }
}
