namespace Monetail.Windows;

internal sealed class StartupContext : ApplicationContext
{
    private readonly MainWindow window = new();
    private readonly System.Windows.Forms.Timer start = new() { Interval = 1 };

    public StartupContext()
    {
        // 非同期の起動処理もUIスレッドへ戻れるよう、表示前にハンドルを作る。
        _ = window.Handle;
        start.Tick += Begin;
        start.Start();
    }

    private async void Begin(object? sender, EventArgs e)
    {
        start.Stop();
        start.Dispose();
        using var progress = new Form
        {
            Text = "Monetail", ClientSize = new Size(240, 80), ControlBox = false,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog
        };
        progress.Controls.Add(new Label { Text = "接続中…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        using var delay = new System.Windows.Forms.Timer { Interval = 1000 };
        delay.Tick += (_, _) => { delay.Stop(); progress.Show(); };
        delay.Start();
        try { await window.InitializeAsync(); }
        finally
        {
            delay.Stop();
            progress.Close();
        }
        if (!window.IsDisposed)
        {
            // Application.Runが起動直後にMainFormを表示しないよう、初期化後に登録する。
            MainForm = window;
            window.Show();
        }
    }
}
