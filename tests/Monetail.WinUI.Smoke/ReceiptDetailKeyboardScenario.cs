using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Windows.System;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task PressReceiptKey(ushort key, ushort modifier = 0)
    {
        await ActivateReceiptInputWindowAsync();
        ReceiptNativeInput Key(ushort code, bool up) => new() { Type = 1, Data = new() { Key = new() { VirtualKey = code, Flags = up ? 2u : 0u } } };
        var events = new List<ReceiptNativeInput>(); if (modifier != 0) events.Add(Key(modifier, false));
        events.Add(Key(key, false)); events.Add(Key(key, true)); if (modifier != 0) events.Add(Key(modifier, true));
        if (SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<ReceiptNativeInput>()) != events.Count) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private async Task CheckReceiptDetailKeyboard(FrameworkElement root)
    {
        var editor = ReceiptEditing!; var group = editor.SelectedGroup!;
        var name = Find<TextBox>(root, "ReceiptItemName"); var price = Find<TextBox>(root, "ReceiptItemPrice");
        var keyboard = (FormKeyboard)typeof(MainWindow).GetField("receiptEditorKeyboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        name.Focus(FocusState.Keyboard); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name));
        await PressReceiptKey(0x0D); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), price));
        await PressReceiptKey(0x0D, 0x10); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name));
        Check(true, "明細のEnter/Shift+Enterは確定順で次/前の欄へ移動");
        // 合成IME状態で共通ヘルパーの変換保護を確認。実IME製品の手動入力は別途必要。
        var composing = (HashSet<TextBox>)typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboard)!;
        composing.Add(name);
        Check(keyboard.HandleEnter(false, false) is null && keyboard.IsComposing, "明細入力のIME変換中はEnter移動を行わない"); composing.Clear();
        var tax = Find<ComboBox>(root, "ReceiptItemTax"); tax.Focus(FocusState.Keyboard); tax.IsDropDownOpen = true; await Settle();
        Check(keyboard.HandleEnter(false, false) is null, "明細税率ComboBox展開中は標準キー操作を優先"); tax.IsDropDownOpen = false;
        name.Text = "キー追加"; price.Text = "1"; tax.SelectedIndex = 0;
        var people = Find<StackPanel>(root, "ReceiptDetailPeople").Children.OfType<CheckBox>().ToArray();
        people[0].Focus(FocusState.Keyboard); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), people[0]));
        var before = people.Select(p => p.IsChecked).ToArray();
        await PressReceiptKey(0x28); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), people[1]));
        Check(people.Select(p => p.IsChecked).SequenceEqual(before), "人物の下矢印はフォーカスだけ移動しチェックを変えない");
        await PressReceiptKey(0x20); await Wait(() => people[1].IsChecked != before[1]);
        Check(true, "人物のSpaceはフォーカスした一人だけ切替");
        await PressReceiptKey(0x09); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Find<TextBox>(root, "ReceiptItemMemo")));
        await PressReceiptKey(0x09, 0x10); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), people[1]));
        Check(true, "人物領域のTab/Shift+Tabはチェック間を巡回せず領域外へ移動");
        name.Focus(FocusState.Keyboard); var count = group.Details.Count;
        await PressReceiptKey(0x0D, 0x11); await Settle();
        Check(group.Details.Count == count && !Find<Button>(root, "ReceiptRegister").IsEnabled, "Ctrl+Enterは全体保存も明細追加も実行しない");
        await PressReceiptKey(0x0D, 0x12); await Wait(() => group.Details.Count == count + 1);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name), "Alt+Enterで追加し品名へ戻す");
        var list = Find<ListView>(root, "ReceiptDetailsList"); list.SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        list.ScrollIntoView(list.Items[0]); await Wait(() => list.ContainerFromIndex(0) is ListViewItem { IsLoaded: true });
        ((ListViewItem)list.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
        await PressReceiptKey(0x28); await Wait(() => editor.SelectedDetailKey == group.Details[1].Key);
        Check(name.Text == group.Details[1].Values.Name, "明細一覧の矢印は選択明細をフォームへ復元");
        await PressReceiptKey(0x0D); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), name));
        Check(true, "明細一覧のEnterは品名へフォーカス移動");
    }
}
