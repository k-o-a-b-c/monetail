using System.Runtime.InteropServices;
using Monetail.Windows.Controls;
using Xunit;

namespace Monetail.Windows.Tests;

public sealed class InputControlTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("combo")]
    [InlineData("check")]
    [InlineData("radio")]
    [InlineData("list")]
    [InlineData("date")]
    [InlineData("time")]
    public Task EnterAndShiftEnterFollowTabOrderForStandardInputs(string kind) => OnUiThread(() =>
    {
        using var form = new TestForm();
        Control first = kind switch
        {
            "combo" => new ComboBox(), "check" => new CheckBox(), "radio" => new RadioButton(),
            "list" => new ListBox(), "date" => new DateInput(), "time" => new TimeInput(), _ => new TextBox()
        };
        var second = new TextBox();
        form.Add(first, second); form.Show(); first.Focus();
        Assert.True(first.ContainsFocus);
        Assert.True(form.Press(Keys.Enter)); Assert.True(second.Focused);
        Assert.True(form.Press(Keys.Shift | Keys.Enter)); Assert.True(first.ContainsFocus);
    });

    [Fact]
    public Task LastInputMovesToSaveWithoutSavingAndDisabledFieldsAreSkipped() => OnUiThread(() =>
    {
        using var form = new TestForm();
        var first = new TextBox();
        form.Add(first, new TextBox { Enabled = false }, new TextBox { Visible = false }, form.Save);
        form.Show(); first.Focus();
        form.Press(Keys.Enter);
        Assert.True(form.Save.Focused); Assert.Equal(0, form.Saves);
        form.Press(Keys.Enter); Assert.Equal(1, form.Saves);
        form.Save.Enabled = false; first.Focus();
        form.Press(Keys.Control | Keys.Enter); Assert.Equal(1, form.Saves);
    });

    [Fact]
    public Task CtrlEnterSavesOnlyWhenRegisteredAndEnabled() => OnUiThread(() =>
    {
        using var form = new TestForm(); var input = new TextBox();
        form.Add(input, form.Save); form.Show(); input.Focus();
        Assert.True(form.Press(Keys.Control | Keys.Enter)); Assert.Equal(1, form.Saves);
        form.SubmitButton = null;
        form.Press(Keys.Control | Keys.Enter); Assert.Equal(1, form.Saves);
    });

    [Fact]
    public Task MultilineAndGridKeepTheirEnterWhileTabKeepsNavigating() => OnUiThread(() =>
    {
        using var form = new TestForm();
        var multiline = new TextBox { Multiline = true, AcceptsReturn = true };
        var grid = new DataGridView { Width = 250, Height = 100 };
        grid.Columns.Add("value", "値"); grid.Rows.Add("入力");
        form.Add(multiline, grid, form.Save); form.Show(); multiline.Focus();
        Assert.False(form.Press(Keys.Enter)); Assert.True(multiline.Focused); Assert.Equal(0, form.Saves);
        grid.Focus(); grid.CurrentCell = grid[0, 0]; grid.BeginEdit(true);
        Assert.NotNull(grid.EditingControl);
        Assert.False(form.Press(Keys.Enter, grid.EditingControl)); Assert.Equal(0, form.Saves);
        grid.EndEdit(); multiline.Focus();
        form.Press(Keys.Tab); Assert.True(grid.ContainsFocus);
        form.Press(Keys.Shift | Keys.Tab); Assert.True(multiline.Focused);
    });

    [Fact]
    public Task ComboCandidateEnterCommitsWithoutMovingOrSaving() => OnUiThread(() =>
    {
        using var form = new TestForm();
        var combo = new ComboBox(); combo.Items.AddRange(["父", "母"]);
        var next = new TextBox(); form.Add(combo, next, form.Save); form.Show(); combo.Focus();
        combo.DroppedDown = true; combo.SelectedIndex = 1;
        Assert.False(form.Press(Keys.Enter));
        // 標準メッセージループと同じく、KeyDownとTranslateMessage相当のWM_CHARを送る。
        var edit = GetWindow(combo.Handle, 5);
        SendMessage(edit, 0x0100, (IntPtr)Keys.Enter, IntPtr.Zero);
        SendMessage(edit, 0x0102, (IntPtr)Keys.Enter, IntPtr.Zero);
        Application.DoEvents();
        Assert.False(combo.DroppedDown); Assert.Equal("母", combo.Text);
        Assert.True(combo.ContainsFocus); Assert.Equal(0, form.Saves);
        form.Press(Keys.Enter); Assert.True(next.Focused);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NativeImeCompositionPreventsEnterMovementAndCtrlEnterSave(bool comboInput) => OnUiThread(() =>
    {
        using var form = new TestForm();
        Control input = comboInput ? new ComboBox() : new TextBox();
        form.Add(input, form.Save); form.Show(); input.Focus();
        var native = comboInput ? GetWindow(input.Handle, 5) : input.Handle;
        SendMessage(native, 0x010D, IntPtr.Zero, IntPtr.Zero);
        Assert.False(form.Press(Keys.Enter, input));
        Assert.False(form.Press(Keys.Control | Keys.Enter, input));
        Assert.True(input.ContainsFocus); Assert.Equal(0, form.Saves);
        SendMessage(native, 0x010E, IntPtr.Zero, IntPtr.Zero);
        form.Press(Keys.Control | Keys.Enter, input); Assert.Equal(1, form.Saves);
    });

    [Fact]
    public Task DateCommitUsesLocalDateAndOptionalInvalidInputBecomesEmpty() => OnUiThread(() =>
    {
        using var form = new TestForm();
        var date = new DateInput { TimeProvider = new FixedTime(), AllowEmpty = true };
        var next = new TextBox(); form.Add(date, next); form.Show(); date.Focus();
        date.Text = "２"; Assert.Equal("２", date.Text);
        form.Press(Keys.Enter); Assert.Equal("2026/11/02", date.Text);
        date.Focus(); date.Text = "4/31"; form.Press(Keys.Enter); Assert.Equal("", date.Text);
        date.AllowEmpty = false; date.Text = "不正";
        Assert.Equal(new DateOnly(2026, 11, 2), date.Commit()); Assert.Equal("2026/11/02", date.Text);
    });

    [Fact]
    public Task TimeCommitSeparatesEmptyFromExplicitMidnight() => OnUiThread(() =>
    {
        using var form = new TestForm(); var time = new TimeInput { AllowEmpty = true };
        var next = new TextBox(); form.Add(time, next); form.Show(); time.Focus();
        time.Text = "９：５"; form.Press(Keys.Enter); Assert.Equal("09:05:00", time.Text);
        time.Text = "24:00"; Assert.Null(time.Commit()); Assert.Equal("", time.Text);
        time.Text = "0"; Assert.Equal(TimeOnly.MinValue, time.Commit()); Assert.Equal("00:00:00", time.Text);
        time.AllowEmpty = false; time.Text = ""; Assert.Equal(TimeOnly.MinValue, time.Commit());
    });

    [Fact]
    public Task CancelDoesNotNormalizeTheInputBeingDiscarded() => OnUiThread(() =>
    {
        using var form = new TestForm(); var date = new DateInput();
        var cancel = new Button { Text = "キャンセル", CausesValidation = false };
        string? discarded = null; cancel.Click += (_, _) => discarded = date.Text;
        form.Add(date, cancel); form.Show(); date.Focus(); date.Text = "4/31";
        cancel.Focus(); cancel.PerformClick();
        Assert.Equal("4/31", discarded); Assert.Equal("4/31", date.Text);
    });

    [Fact]
    public Task ReadOnlyDateDoesNotRewriteItsDisplay() => OnUiThread(() =>
    {
        using var date = new DateInput { ReadOnly = true, Text = "261201", TimeProvider = new FixedTime() };
        Assert.Equal(new DateOnly(2026, 12, 1), date.Commit()); Assert.Equal("261201", date.Text);
    });

    private sealed class TestForm : InputForm
    {
        public readonly Button Save = new() { Text = "保存" };
        public int Saves;
        private readonly FlowLayoutPanel layout = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        public TestForm()
        {
            Controls.Add(layout); Save.Click += (_, _) => Saves++;
            SubmitButton = Save; AcceptButton = Save;
        }
        public void Add(params Control[] controls)
        {
            for (var index = 0; index < controls.Length; index++)
            { controls[index].TabIndex = index; layout.Controls.Add(controls[index]); }
        }
        public bool Press(Keys key, Control? source = null)
        {
            Application.DoEvents();
            var focused = source ?? layout.Controls.Cast<Control>().FirstOrDefault(control => control.ContainsFocus);
            var message = Message.Create(focused?.Handle ?? Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            var result = ProcessCmdKey(ref message, key) || ProcessDialogKey(key);
            Application.DoEvents(); return result;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 16, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Synthetic+09", TimeSpan.FromHours(9), "Test", "Test");
    }

    private static Task OnUiThread(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);
}
