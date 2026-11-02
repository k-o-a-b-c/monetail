namespace Monetail.Windows.Controls;

/// <summary>標準レイアウトだけで見出し・操作列・伸縮する本文を配置する。</summary>
internal sealed class PageLayout : Panel
{
    private readonly Font bodyFont;
    private readonly Font headingFont;
    private readonly TableLayoutPanel layout = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, RowCount = 0, ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
        Padding = new Padding(24, 20, 24, 20), Margin = Padding.Empty, TabStop = false
    };

    public PageLayout(Font font)
    {
        bodyFont = new Font(font.FontFamily, 10);
        headingFont = new Font(font.FontFamily, 16);
        Font = bodyFont;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Controls.Add(layout);
        TabStop = false;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // スクロール範囲の計算前に更新し、縮小前の高さを残さない。
        layout.MinimumSize = new Size(0, ClientSize.Height);
        base.OnLayout(e);
    }

    public void AddSection(Control control, bool expand = false)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(expand ? SizeType.Percent : SizeType.AutoSize, expand ? 100 : 0));
        control.Dock = DockStyle.Fill;
        control.TabIndex = row;
        layout.Controls.Add(control, 0, row);
    }

    public void AddHeading(string title, string ledgerName)
    {
        var header = Stack();
        header.Margin = new Padding(0, 0, 0, 16);
        var heading = TextLabel(title);
        heading.Font = headingFont;
        header.Controls.Add(heading);
        header.Controls.Add(TextLabel(ledgerName));
        AddSection(header);
    }

    public static TableLayoutPanel Stack() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
        Dock = DockStyle.Top, Margin = Padding.Empty, TabStop = false
    };

    public static FlowLayoutPanel Actions() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), TabStop = false
    };

    public static Label TextLabel(string text) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill,
        Margin = new Padding(0, 0, 0, 8), UseMnemonic = false
    };

    public static TableLayoutPanel Fields() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2, ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) },
        MaximumSize = new Size(720, 0), Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4), TabStop = false
    };

    public static Label AddField(TableLayoutPanel fields, string title, TextBox input)
    {
        var row = fields.RowCount;
        fields.RowCount += 2;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = TextLabel(title);
        label.Dock = DockStyle.None;
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 0, 16, 12);
        label.TabIndex = row;
        fields.Controls.Add(label, 0, row);
        input.AccessibleName = title;
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        input.Margin = new Padding(0, 0, 0, 12);
        input.TabIndex = row + 1;
        fields.Controls.Add(input, 1, row);
        var error = Notice();
        fields.Controls.Add(error, 1, row + 1);
        return error;
    }

    public static Label Notice(string? text = null)
    {
        var label = TextLabel(text ?? "");
        label.Visible = text is not null;
        label.AccessibleRole = AccessibleRole.Alert;
        // 色だけに依存せず説明文で伝え、ハイコントラストの文字色を尊重する。
        label.ForeColor = SystemColors.ControlText;
        return label;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { headingFont.Dispose(); bodyFont.Dispose(); }
    }
}
