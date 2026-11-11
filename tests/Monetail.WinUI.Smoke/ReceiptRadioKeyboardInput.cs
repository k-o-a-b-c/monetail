using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private async Task PressReceiptRadioKeyAsync(RadioButton radio, ushort key)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window!);
        window!.Activate(); SetForegroundWindow(handle);
        await Wait(() => GetForegroundWindow() == handle);
        if (!radio.Focus(FocusState.Keyboard)) throw new InvalidOperationException("RadioButtonへキーボードフォーカスを移動できません。");
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(radio.XamlRoot), radio));
        var inputs = new[] {
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { VirtualKey = key } } },
            new ReceiptNativeInput { Type = 1, Data = new() { Key = new() { VirtualKey = key, Flags = 2 } } }
        };
        if (SendInput(2, inputs, Marshal.SizeOf<ReceiptNativeInput>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
