using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows;

public sealed partial class MainWindow
{
    private LedgerWorkspace? ledgers;
    private AuthenticatedAccount? currentAccount;
    private LedgerInfo? openedLedger;
    private LedgerCreationDraft? creationDraft;

    private void SetLedgerClient()
    {
        ledgers = new LedgerWorkspace(api!, session!);
        ClearLedgerState();
    }

    private void ClearLedgerState()
    {
        currentAccount = null;
        openedLedger = null;
        creationDraft = null;
    }

    private async Task ShowWorkspaceAsync(AuthenticatedAccount account, CancellationToken token = default)
    {
        if (currentAccount?.AuthUserId != account.AuthUserId) ClearLedgerState();
        currentAccount = account;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        timeout.CancelAfter(MonetailApiClient.ConnectionTimeout);
        var destination = await ledgers!.StartAsync(timeout.Token);
        if (destination.Opened is not null) ShowLedgerHome(destination.Opened);
        else if (destination.Choices.Count == 0) ShowLedgerCreation(noMembership: true);
        else ShowLedgerSelection(destination.Choices);
    }

    private void AddAccountActions()
    {
        content.Controls.Add(new Label { AutoSize = true, Text = $"{currentAccount!.DisplayName} さん", Margin = new Padding(0, 16, 0, 4) });
        Button("ログアウト", () => RunAsync(async () =>
        {
            await session!.LogoutAsync(lifetime.Token);
            ShowLogin();
        }, () => InitializeAsync()));
    }

    private void ShowLedgerSelection(IReadOnlyList<LedgerInfo> choices)
    {
        Reset("家計簿の選択", "開く家計簿を選んでください。");
        var list = new ListBox { Width = 440, Height = 120, DisplayMember = nameof(LedgerInfo.Name), AccessibleName = "参加している家計簿" };
        foreach (var ledger in choices) list.Items.Add(ledger);
        content.Controls.Add(list);
        Task OpenSelectionAsync() => list.SelectedItem is LedgerInfo ledger ? OpenLedgerAsync(ledger.LedgerId) : Task.CompletedTask;
        var open = Button("開く", OpenSelectionAsync);
        open.Enabled = false;
        list.SelectedIndexChanged += (_, _) => open.Enabled = list.SelectedItem is LedgerInfo;
        list.DoubleClick += async (_, _) => await OpenSelectionAsync();
        AcceptButton = open;
        AddButton("＋追加", () => ShowLedgerCreation(noMembership: choices.Count == 0));
        if (openedLedger is not null) AddButton("戻る", () => ShowLedgerHome(openedLedger));
        AddAccountActions();
    }

    private void ShowLedgerCreation(bool noMembership, string? message = null)
    {
        Reset("家計簿の作成", message ?? (noMembership
            ? "参加している家計簿はありません。\n参加する場合は、管理者から招待リンクを受け取ってください。"
            : "新しい家計簿の名前を入力してください。"));
        content.Controls.Add(new Label { AutoSize = true, Text = "家計簿名" });
        var name = new TextBox { Width = 440, Text = creationDraft?.Name ?? "", AccessibleName = "家計簿名" };
        content.Controls.Add(name);
        var create = Button("作成", () => CreateLedgerAsync(name.Text, noMembership));
        AcceptButton = create;
        if (openedLedger is not null) AddButton("キャンセル", () => ShowLedgerHome(openedLedger));
        else if (!noMembership) Button("キャンセル", ShowLedgerListAsync);
        AddAccountActions();
        name.Focus();
    }

    private Task CreateLedgerAsync(string name, bool noMembership)
    {
        try
        {
            // 同じ名前の通信再試行では要求IDを維持する。名前を変えた操作だけ新しく発行する。
            if (creationDraft?.Name != name.Trim()) creationDraft = new LedgerCreationDraft(name);
        }
        catch (FormatException error) { ShowLedgerCreation(noMembership, error.Message); return Task.CompletedTask; }
        var draft = creationDraft!;
        return RunAsync(async () =>
        {
            var ledger = await ledgers!.CreateAsync(draft, lifetime.Token);
            creationDraft = null;
            ShowLedgerHome(ledger);
        }, () => CreateLedgerAsync(draft.Name, noMembership));
    }

    private Task OpenLedgerAsync(Guid ledgerId) => RunAsync(async () =>
    {
        var ledger = await ledgers!.OpenAsync(ledgerId, lifetime.Token);
        ShowLedgerHome(ledger);
    }, () => OpenLedgerAsync(ledgerId));

    private Task ShowLedgerListAsync() => RunAsync(async () =>
    {
        var choices = await ledgers!.ListAsync(lifetime.Token);
        if (choices.Length == 0) ShowLedgerCreation(noMembership: true);
        else ShowLedgerSelection(choices);
    }, ShowLedgerListAsync);

    private void ShowLedgerHome(LedgerInfo ledger)
    {
        openedLedger = ledger;
        Reset("Monetail", null);
        var selector = AddButton(ledger.Name + " ▾", () => { });
        selector.Click += async (_, _) => await RunAsync(async () =>
        {
            var choices = await ledgers!.ListAsync(lifetime.Token);
            var menu = new ContextMenuStrip();
            foreach (var choice in choices)
            {
                var item = new ToolStripMenuItem(choice.Name) { Checked = choice.LedgerId == ledger.LedgerId };
                item.Click += async (_, _) => await OpenLedgerAsync(choice.LedgerId);
                menu.Items.Add(item);
            }
            if (choices.Length > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("＋追加", null, (_, _) => ShowLedgerCreation(noMembership: choices.Length == 0));
            // メニューの項目クリックはClosedの後に通知されるため、破棄は次のUI処理へ回す。
            menu.Closed += (_, _) => BeginInvoke((Action)menu.Dispose);
            menu.Show(selector, new Point(0, selector.Height));
        }, () => OpenLedgerAsync(ledger.LedgerId));
        content.Controls.Add(new Label { AutoSize = true, Text = "レシート一覧", Margin = new Padding(0, 16, 0, 8) });
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(440, 0), Text = "レシートの一覧・入力機能は今後追加します。" });
        AddAccountActions();
    }

    private void ShowLedgerFailure(LedgerRequestException error, Func<Task> retry)
    {
        var message = error.StatusCode switch
        {
            HttpStatusCode.NotFound => "この家計簿を開けません。参加している家計簿を確認してください。",
            HttpStatusCode.Conflict => "作成要求を確認できませんでした。家計簿一覧を確認してください。",
            HttpStatusCode.TooManyRequests => "操作が続いています。しばらく待ってから再試行してください。",
            _ => "家計簿の操作を完了できませんでした。入力内容を確認してください。"
        };
        Reset("Monetail", message);
        Button("再試行", retry);
        Button("家計簿一覧", ShowLedgerListAsync);
        if (error.StatusCode == HttpStatusCode.BadRequest) AddButton("名前を変更", () => ShowLedgerCreation(noMembership: openedLedger is null));
        AddAccountActions();
    }
}
