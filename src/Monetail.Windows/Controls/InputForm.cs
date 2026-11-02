using System.ComponentModel;

namespace Monetail.Windows.Controls;

/// <summary>標準コントロールの操作を尊重してEnter移動・登録ショートカットを共用する。</summary>
public class InputForm : Form
{
    private readonly HashSet<Control> observed = [];
    private readonly Dictionary<Control, ImeComposition> compositions = [];

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Button? SubmitButton { get; set; }

    public InputForm() => ControlAdded += ObserveAdded;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) != Keys.Enter) return base.ProcessCmdKey(ref msg, keyData);
        var source = FocusedControl(msg.HWnd);
        if (BlocksEnter(source)) return false;
        if (keyData == (Keys.Control | Keys.Enter) && SubmitButton is { } submit)
        {
            if (submit.Enabled && submit.Visible) submit.PerformClick();
            return true;
        }
        if (keyData is not (Keys.Enter or (Keys.Shift | Keys.Enter))) return base.ProcessCmdKey(ref msg, keyData);
        if (UsesStandardEnter(source)) return false;
        if (source is TextBoxBase or ComboBox or CheckBox or RadioButton or ListBox ||
            source is Button && keyData == (Keys.Shift | Keys.Enter))
        {
            SelectNextControl(source, keyData == Keys.Enter, true, true, true);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Enter)
        {
            var source = FocusedControl(IntPtr.Zero);
            // AcceptButtonによる保存も避け、IME・候補選択・改行を元のコントロールへ返す。
            if (BlocksEnter(source) || UsesStandardEnter(source)) return false;
        }
        return base.ProcessDialogKey(keyData);
    }

    private Control? FocusedControl(IntPtr handle)
    {
        var control = handle == IntPtr.Zero ? null : Control.FromChildHandle(handle);
        if (control is not null && control != this) return control;
        control = ActiveControl;
        while (control is ContainerControl container && container.ActiveControl is { } nested) control = nested;
        return control;
    }

    private bool BlocksEnter(Control? source) => source is not null &&
        (compositions.TryGetValue(source, out var state) && state.BlocksEnter || source is ComboBox { DroppedDown: true });

    private static bool UsesStandardEnter(Control? source)
    {
        if (source is TextBoxBase { Multiline: true }) return true;
        for (var control = source; control is not null; control = control.Parent)
            if (control is DataGridView) return true;
        return false;
    }

    private void ObserveAdded(object? sender, ControlEventArgs e) => Observe(e.Control!);
    private void Observe(Control control)
    {
        if (!observed.Add(control)) return;
        control.ControlAdded += ObserveAdded;
        control.HandleCreated += InputHandleCreated;
        control.HandleDestroyed += InputHandleDestroyed;
        control.Disposed += ControlDisposed;
        if (control.IsHandleCreated) RegisterComposition(control);
        foreach (Control child in control.Controls) Observe(child);
    }

    private void InputHandleCreated(object? sender, EventArgs e) => RegisterComposition((Control)sender!);
    private void InputHandleDestroyed(object? sender, EventArgs e) => RemoveComposition((Control)sender!);
    private void RegisterComposition(Control control)
    {
        RemoveComposition(control);
        if (control is TextBoxBase or ComboBox) compositions.Add(control, new ImeComposition(control));
    }
    private void RemoveComposition(Control control)
    {
        if (compositions.Remove(control, out var state)) state.Dispose();
    }
    private void ControlDisposed(object? sender, EventArgs e)
    {
        var control = (Control)sender!;
        RemoveComposition(control);
        observed.Remove(control);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var control in observed)
            {
                control.ControlAdded -= ObserveAdded; control.HandleCreated -= InputHandleCreated;
                control.HandleDestroyed -= InputHandleDestroyed; control.Disposed -= ControlDisposed;
            }
            observed.Clear();
            foreach (var state in compositions.Values) state.Dispose();
            compositions.Clear();
        }
        base.Dispose(disposing);
    }
}
