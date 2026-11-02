using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Monetail.WinUI.Input;

/// <summary>標準のTab順とIMEを保ち、単一行入力のEnterだけを次のフォーカスへ変える。</summary>
internal sealed class FormKeyboard
{
    private readonly FrameworkElement root;
    private readonly Button? saveButton;
    private readonly Func<Task>? save;
    private readonly HashSet<TextBox> composing = [];
    private bool completingWithEnter;

    public FormKeyboard(FrameworkElement root, Button? saveButton, Func<Task>? save, params TextBox[] inputs)
    {
        this.root = root;
        this.saveButton = saveButton;
        this.save = save;
        root.KeyDown += OnKeyDown;
        root.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key == VirtualKey.Enter) completingWithEnter = false;
        }), true);
        foreach (var input in inputs)
        {
            input.TextCompositionStarted += (_, _) => composing.Add(input);
            input.TextCompositionEnded += (_, _) =>
            {
                composing.Remove(input);
                // 変換終了がKeyDownより先に通知されても、その確定キーでは移動・保存しない。
                completingWithEnter |= Down(VirtualKey.Enter);
            };
            input.LostFocus += (_, _) => composing.Remove(input);
        }
    }

    public bool IsComposing => composing.Count != 0 || completingWithEnter;

    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || args.Key != VirtualKey.Enter) return;
        var operation = HandleEnter(Down(VirtualKey.Control), Down(VirtualKey.Shift), Down(VirtualKey.Menu));
        if (operation is null) return;
        args.Handled = true;
        await operation;
    }

    internal Task? HandleEnter(bool control, bool shift, bool alt = false)
    {
        if (alt || IsComposing || root.XamlRoot is null) return null;
        var focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
        var input = Ancestor<TextBox>(focused);
        var combo = Ancestor<ComboBox>(focused);
        if (input is { AcceptsReturn: true } || Ancestor<RichEditBox>(focused) is not null ||
            combo is { IsDropDownOpen: true }) return null;
        if (control)
            return saveButton is { IsEnabled: true, Visibility: Visibility.Visible } && save is not null ? save() : null;
        // ボタンや一覧上のEnterは、各コントロールの標準操作に任せる。
        if (input is null && combo is null) return null;
        FocusManager.TryMoveFocus(shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next);
        return Task.CompletedTask;
    }

    private static T? Ancestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool Down(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
