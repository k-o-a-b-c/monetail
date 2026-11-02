using System.Runtime.InteropServices;

namespace Monetail.Windows.Controls;

/// <summary>IME確定のEnterをフォーカス移動や保存に使わないため、ネイティブ入力窓を監視する。</summary>
internal sealed class ImeComposition : IDisposable
{
    private readonly List<InputWindow> windows = [];
    public bool BlocksEnter => windows.Any(window => window.BlocksEnter);

    public ImeComposition(Control control) => Watch(control.Handle);

    private void Watch(IntPtr handle)
    {
        windows.Add(new InputWindow(handle));
        // ComboBoxの編集領域はControlsに現れない子HWNDなので、同じIME監視を付ける。
        for (var child = GetWindow(handle, 5); child != IntPtr.Zero; child = GetWindow(child, 2)) Watch(child);
    }

    public void Dispose()
    {
        foreach (var window in windows) window.ReleaseHandle();
        windows.Clear();
    }

    private sealed class InputWindow : NativeWindow
    {
        private bool composing, completingEnter;
        public bool BlocksEnter => composing || completingEnter;
        public InputWindow(IntPtr handle) => AssignHandle(handle);
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x010D: composing = true; completingEnter = false; break; // WM_IME_STARTCOMPOSITION
                case 0x010E: // WM_IME_ENDCOMPOSITION。確定キーを離すまで保存・移動に転用しない。
                    composing = false; completingEnter = GetKeyState((int)Keys.Enter) < 0; break;
                case 0x0101 when (int)m.WParam == (int)Keys.Enter: completingEnter = false; break;
                case 0x0105 when (int)m.WParam == (int)Keys.Enter: completingEnter = false; break;
                case 0x0008: composing = false; completingEnter = false; break; // WM_KILLFOCUS
            }
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);
}
