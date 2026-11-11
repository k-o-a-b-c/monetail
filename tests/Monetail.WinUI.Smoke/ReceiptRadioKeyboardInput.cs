using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    // 合成CI専用。製品コードには入力注入・キー置換を追加しない。
    [StructLayout(LayoutKind.Sequential)]
    private struct ReceiptNativeInput { public uint Type; public ReceiptInputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct ReceiptInputUnion
    {
        [FieldOffset(0)] public ReceiptKeyInput Key;
        [FieldOffset(0)] public ReceiptMouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ReceiptKeyInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ReceiptMouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] ReceiptNativeInput[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)]
    private struct ReceiptScreenPoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref ReceiptScreenPoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    private async Task ActivateReceiptInputWindowAsync()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window!);
        window!.Activate(); SetForegroundWindow(handle);
        await Wait(() => GetForegroundWindow() == handle);
    }
    private async Task ClickReceiptRadioAsync(RadioButton radio)
    {
        await ActivateReceiptInputWindowAsync();
        // 標準のレイアウト変換と実際の倍率を使ってHWNDのクライアント座標へ変換する。
        radio.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(radio)!;
        await Wait(() => !peer.IsOffscreen());
        // RadioButton全体の余白ではなく、標準テンプレートのラベルをクリックする。
        await Wait(() => All(radio).OfType<TextBlock>().Any(t => Equals(t.Text, radio.Content) && t.ActualWidth > 0));
        var label = All(radio).OfType<TextBlock>().First(t => Equals(t.Text, radio.Content) && t.ActualWidth > 0);
        var logical = label.TransformToVisual(radio.XamlRoot.Content).TransformPoint(new(label.ActualWidth / 2, label.ActualHeight / 2));
        var scale = radio.XamlRoot.RasterizationScale;
        var point = new ReceiptScreenPoint { X = (int)Math.Round(logical.X * scale), Y = (int)Math.Round(logical.Y * scale) };
        if (!ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window!), ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        TraceInvocation($"radio={radio.Name}; mouseScreenPoint={point.X},{point.Y}; scale={scale}");
        if (!SetCursorPos(point.X, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var inputs = new[] {
            new ReceiptNativeInput { Type = 0, Data = new() { Mouse = new() { Flags = 2 } } },
            new ReceiptNativeInput { Type = 0, Data = new() { Mouse = new() { Flags = 4 } } }
        };
        if (SendInput(2, inputs, Marshal.SizeOf<ReceiptNativeInput>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private async Task PressReceiptRadioSpaceAsync(RadioButton radio)
    {
        await ActivateReceiptInputWindowAsync();
        if (!radio.Focus(FocusState.Keyboard)) throw new InvalidOperationException("RadioButtonへキーボードフォーカスを移動できません。");
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(radio.XamlRoot), radio));
        var inputs = new[] {
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { VirtualKey = 0x20 } } },
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { VirtualKey = 0x20, Flags = 2 } } }
        };
        if (SendInput(2, inputs, Marshal.SizeOf<ReceiptNativeInput>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
