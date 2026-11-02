using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (SetForegroundWindow(handle)) return;
        // 前面化がOSに拒否されても認証は成功させ、タスクバーで完了を知らせる。
        var information = new FlashInformation
        {
            Size = (uint)Marshal.SizeOf<FlashInformation>(), Window = handle, Flags = 3, Count = 3
        };
        FlashWindowEx(ref information);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInformation
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
