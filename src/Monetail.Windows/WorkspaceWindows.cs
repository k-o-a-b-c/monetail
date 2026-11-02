using Monetail.Windows.Client.Transport;
using Monetail.Windows.Controls;

namespace Monetail.Windows;

public sealed partial class MainWindow
{
    private PageLayout CreateRecordPage(LedgerInfo ledger, Action openSettings)
    {
        var page = new PageLayout(Font);
        var navigation = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4,
            ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            RowCount = 1, RowStyles = { new RowStyle(SizeType.AutoSize) },
            Margin = new Padding(0, 0, 0, 20), TabStop = false
        };
        var brand = page.HeadingLabel("Monetail");
        brand.Margin = new Padding(0, 0, 24, 0);
        navigation.Controls.Add(brand, 0, 0);
        var recordActions = PageLayout.Actions(); recordActions.Margin = Padding.Empty;
        navigation.Controls.Add(recordActions, 1, 0);
        var record = AddButton("記録", () => ShowLedgerHome(ledger), recordActions);
        record.BackColor = SystemColors.Control;
        record.AccessibleDescription = "現在の画面";
        var analysisActions = PageLayout.Actions(); analysisActions.Margin = Padding.Empty;
        navigation.Controls.Add(analysisActions, 2, 0);
        var analysis = AddButton("集計・分析", () => { }, analysisActions);
        analysis.Enabled = false;
        PageLayout.StyleButton(analysis, quiet: true);
        var settingsActions = PageLayout.Actions(); settingsActions.Margin = Padding.Empty;
        navigation.Controls.Add(settingsActions, 3, 0);
        AddButton("管理・設定", openSettings, settingsActions);
        page.AddSection(navigation);
        var ledgerActions = PageLayout.Actions();
        page.AddSection(ledgerActions);
        AddLedgerSelector(ledger, ledgerActions);
        page.AddSection(PageLayout.Divider());
        var records = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 2,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 64), new ColumnStyle(SizeType.Percent, 36) },
            RowStyles = { new RowStyle(SizeType.Percent, 50), new RowStyle(SizeType.Percent, 50) },
            Margin = Padding.Empty, MinimumSize = new Size(0, 320), TabStop = false
        };
        var receipts = RecordCard(page, "レシート一覧", "＋ 登録", "レシートの一覧・入力機能は今後追加します。");
        records.Controls.Add(receipts, 0, 0); records.SetRowSpan(receipts, 2);
        records.Controls.Add(RecordCard(page, "資金移動", "＋", "資金移動の機能は今後追加します。"), 1, 0);
        records.Controls.Add(RecordCard(page, "残高登録", "＋", "残高登録の機能は今後追加します。"), 1, 1);
        page.AddSection(records, expand: true);
        return page;
    }

    private Panel RecordCard(PageLayout page, string title, string action, string description)
    {
        var card = PageLayout.Stack();
        card.AutoSize = false;
        card.Padding = new Padding(20);
        card.RowCount = 3;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            Margin = new Padding(0, 0, 0, 12), Dock = DockStyle.Fill, TabStop = false
        };
        header.Controls.Add(page.HeadingLabel(title), 0, 0);
        var actions = PageLayout.Actions(); actions.Margin = Padding.Empty;
        header.Controls.Add(actions, 1, 0);
        var button = AddButton(action, () => { }, actions);
        button.AccessibleName = title + "の登録";
        button.Enabled = false;
        PageLayout.StyleButton(button, quiet: true);
        card.Controls.Add(header, 0, 0);
        card.Controls.Add(PageLayout.Divider(), 0, 1);
        var notice = PageLayout.TextLabel(description);
        notice.ForeColor = SystemColors.GrayText;
        notice.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(notice, 0, 2);
        return PageLayout.Card(card);
    }
}
