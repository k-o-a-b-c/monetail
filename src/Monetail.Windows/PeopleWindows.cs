using System.Net;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Controls;

namespace Monetail.Windows;

public sealed partial class MainWindow
{
    // ダイアログの確認だけを差し替えられるようにし、通常の遷移は同じウィンドウ内で行う。
    internal Func<string, bool>? ConfirmPersonDeletion;

    private PeopleWorkspace PeopleClient() => new(api!, session!, openedLedger!.LedgerId);

    private Task OpenPeopleAsync()
    {
        if (busy || openedLedger is null) return Task.CompletedTask;
        var page = PeoplePage("人物管理", backToHome: true);
        var error = PageLayout.Notice();
        page.AddSection(error);
        var actions = PageLayout.Actions();
        page.AddSection(actions);
        Button("再読み込み", OpenPeopleAsync, actions);
        page.AddSection(new Panel(), expand: true);
        return RunPeopleAsync(async () => ShowPeople(await PeopleClient().ListAsync(lifetime.Token)), error);
    }

    private void ShowPeople(PeopleInfo data, string? message = null)
    {
        openedLedger = openedLedger! with { Role = data.CurrentRole };
        var page = PeoplePage("人物管理", backToHome: true);
        var toolbar = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            Margin = new Padding(0, 0, 0, 12), TabStop = false
        };
        var actions = PageLayout.Actions(); actions.Margin = Padding.Empty;
        var secondary = PageLayout.Actions(); secondary.Margin = Padding.Empty;
        toolbar.Controls.Add(actions, 0, 0); toolbar.Controls.Add(secondary, 1, 0);
        page.AddSection(toolbar);
        var list = PeopleList(data.People.OrderByDescending(row => row.PersonId == data.MyPersonId)
            .ThenBy(row => row.DisplayOrder), data.MyPersonId);
        page.AddSection(list, expand: true);
        if (data.People.Length == 0) page.AddSection(PageLayout.TextLabel("人物は登録されていません。"));
        page.AddSection(PageLayout.TextLabel("空欄は期間の制限なし"));
        var error = PageLayout.Notice(message);
        page.AddSection(error);
        var admin = data.CurrentRole == "Admin";
        var add = AddButton("追加", () => { if (!busy) ShowPersonEditor(data, null); }, actions);
        add.Enabled = admin;
        var edit = AddButton("編集", () => { if (!busy && SelectedPerson(list) is { } row) ShowPersonEditor(data, row); }, actions);
        var delete = Button("削除", async () =>
        {
            if (busy || SelectedPerson(list) is not { } row) return;
            var confirmed = ConfirmPersonDeletion?.Invoke(row.Name) ?? MessageBox.Show(this,
                $"「{row.Name}」を削除しますか？", "人物の削除", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK;
            if (!confirmed) return;
            await RunPeopleAsync(async () => ShowPeople(await PeopleClient().DeleteAsync(row, lifetime.Token)), error);
        }, actions);
        void SelectionChanged()
        {
            var selected = SelectedPerson(list);
            edit.Enabled = selected is not null && (admin || selected.PersonId == data.MyPersonId);
            delete.Enabled = selected is not null && admin;
        }
        list.SelectedIndexChanged += (_, _) => { if (!busy) SelectionChanged(); };
        SelectionChanged();
        var order = AddButton("並び順を変更", () => { if (!busy) ShowPeopleOrder(data); }, secondary);
        order.Enabled = admin;
    }

    private void ShowPersonEditor(PeopleInfo data, PersonInfo? person)
    {
        var admin = data.CurrentRole == "Admin";
        var page = PeoplePage($"人物の{(person is null ? "追加" : "編集")}");
        var fields = PageLayout.Fields();
        page.AddSection(fields);
        var name = new TextBox { Text = person?.Name ?? "" };
        var nameError = PageLayout.AddField(fields, "人物名", name);
        var from = new DateInput { Text = PersonDate(person?.ValidFrom), AllowEmpty = true };
        var fromError = PageLayout.AddField(fields, "有効開始日", from);
        var to = new DateInput { Text = PersonDate(person?.ValidTo), AllowEmpty = true };
        var toError = PageLayout.AddField(fields, "有効終了日", to);
        from.ReadOnly = to.ReadOnly = !admin;
        page.AddSection(PageLayout.TextLabel("日付は年/月/日で入力してください。期間の空欄は制限なしです。"));
        var error = PageLayout.Notice();
        page.AddSection(error);
        var latestPanel = PageLayout.Stack();
        latestPanel.Visible = false;
        latestPanel.AccessibleName = "最新の保存内容";
        page.AddSection(latestPanel);
        var actions = PageLayout.Actions();
        actions.Margin = new Padding(0, 8, 0, 0);
        page.AddSection(actions);
        page.AddSection(new Panel(), expand: true);
        PersonCreationDraft? draft = null;
        PersonInfo? latest = null;
        Button? save = null;
        void FieldError(string? field, string message)
        {
            var label = field switch { "name" => nameError, "validFrom" => fromError, "validTo" => toError, _ => error };
            label.Text = message; label.Visible = true;
            (field switch { "name" => name, "validFrom" => from, "validTo" => to, _ => name }).Focus();
        }
        void ClearErrors()
        {
            foreach (var label in new[] { nameError, fromError, toError, error }) label.Visible = false;
        }
        void ShowLatest(PersonInfo value)
        {
            latest = value;
            while (latestPanel.Controls.Count > 0) latestPanel.Controls[0].Dispose();
            foreach (var text in new[] { "ほかの操作で変更されています。最新の保存内容：", $"人物名：{value.Name}",
                $"有効開始日：{PersonDate(value.ValidFrom)}", $"有効終了日：{PersonDate(value.ValidTo)}",
                "切り替えると、今回の未保存入力は置き換わります。" })
                latestPanel.Controls.Add(PageLayout.TextLabel(text));
            var replaceActions = PageLayout.Actions();
            latestPanel.Controls.Add(replaceActions);
            AddButton("最新内容に切り替える", () =>
            {
                if (busy || latest is null) return;
                person = latest;
                name.Text = latest.Name; from.Text = PersonDate(latest.ValidFrom); to.Text = PersonDate(latest.ValidTo);
                latest = null; latestPanel.Visible = false; save!.Enabled = true; ClearErrors();
            }, replaceActions);
            latestPanel.Visible = true;
            save!.Enabled = false;
            // RunAsyncの復元対象からも外し、最新内容へ切り替えるまで保存を止める。
            temporarilyDisabled.Remove(save);
        }
        save = Button(person is null ? "登録" : "保存", async () =>
        {
            if (busy || latest is not null) return;
            ClearErrors();
            PersonInput input;
            try { input = new(name.Text, from.Commit(), to.Commit()); }
            catch (PersonInputException invalid) { FieldError(invalid.Field, invalid.Message); return; }
            if (person is null && draft?.Input != input) draft = new(input);
            await RunPeopleAsync(async () =>
            {
                var result = person is null ? await PeopleClient().CreateAsync(draft!, lifetime.Token) :
                    await PeopleClient().UpdateAsync(person, input, admin, lifetime.Token);
                ShowPeople(result);
            }, error, rejected =>
            {
                if (rejected.LatestPerson is { } value && value.PersonId == person?.PersonId) ShowLatest(value);
                else FieldError(rejected.Field, PeopleError(rejected));
            });
        }, actions);
        AcceptButton = save;
        SubmitButton = save;
        // 閉じた後の権限で一覧を取り直す。保存の成功経路は応答の一覧を使う。
        Button("キャンセル", OpenPeopleAsync, actions).CausesValidation = false;
        name.Focus();
    }

    private void ShowPeopleOrder(PeopleInfo data, string? message = null)
    {
        var page = PeoplePage("並び順設定");
        page.AddSection(PageLayout.TextLabel("全員で共有する並び順です。人物を選択して移動してください。"));
        var rows = data.People.OrderBy(row => row.DisplayOrder).ToList();
        var list = PeopleList(rows, null);
        page.AddSection(list, expand: true);
        var moveActions = PageLayout.Actions();
        page.AddSection(moveActions);
        var error = PageLayout.Notice(message);
        page.AddSection(error);
        var actions = PageLayout.Actions();
        actions.Margin = new Padding(0, 4, 0, 0);
        page.AddSection(actions);
        Button? up = null, down = null;
        void UpdateButtons()
        {
            var index = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
            up!.Enabled = index > 0; down!.Enabled = index >= 0 && index < rows.Count - 1;
        }
        void Move(int offset)
        {
            if (busy || list.SelectedIndices.Count == 0) return;
            var index = list.SelectedIndices[0]; var target = index + offset;
            if (target < 0 || target >= rows.Count) return;
            (rows[index], rows[target]) = (rows[target], rows[index]);
            list.BeginUpdate(); list.Items.Clear(); AddPeopleRows(list, rows, null);
            list.Items[target].Selected = true; list.Items[target].Focused = true; list.EndUpdate();
            UpdateButtons();
        }
        up = AddButton("上へ", () => Move(-1), moveActions);
        down = AddButton("下へ", () => Move(1), moveActions);
        list.SelectedIndexChanged += (_, _) => { if (!busy) UpdateButtons(); };
        UpdateButtons();
        var save = Button("保存", () => RunPeopleAsync(async () =>
            ShowPeople(await PeopleClient().SaveOrderAsync(data.OrderRevision, rows.Select(row => row.PersonId).ToArray(), lifetime.Token)),
            error, rejected =>
            {
                if (rejected.LatestPeople is { } current)
                    ShowPeopleOrder(current, "ほかの操作で変更されています。最新の並び順を読み込みます。");
                else { error.Text = PeopleError(rejected); error.Visible = true; }
            }), actions);
        AcceptButton = save;
        SubmitButton = save;
        Button("キャンセル", OpenPeopleAsync, actions).CausesValidation = false;
    }

    private Task RunPeopleAsync(Func<Task> action, Label error, Action<PeopleRequestException>? rejected = null) =>
        RunAsync(async () =>
        {
            var inputs = PageControls().Where(control => control is TextBox or ListView && control.Enabled).ToArray();
            foreach (var control in inputs) control.Enabled = false;
            error.Visible = false;
            try { await action(); }
            catch (PeopleRequestException failure)
            {
                if (rejected is not null) rejected(failure);
                else { error.Text = PeopleError(failure); error.Visible = true; }
            }
            catch (Exception failure) when (failure is ApiConnectionException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                error.Text = "通信または保存先を確認できませんでした。入力を残しています。もう一度操作してください。";
                error.Visible = true;
            }
            finally { foreach (var control in inputs.Where(control => !control.IsDisposed)) control.Enabled = true; }
        }, () => Task.CompletedTask);

    private static string PeopleError(PeopleRequestException error) => error.StatusCode switch
    {
        HttpStatusCode.Forbidden => "この操作を行う権限がなくなりました。変更は保存されていません。",
        HttpStatusCode.NotFound => "対象が見つからないか、この家計簿に参加していません。変更は保存されていません。",
        HttpStatusCode.TooManyRequests => "操作が続いています。しばらく待ってから再試行してください。",
        _ => error.Code switch
        {
            "duplicate_person_name" => "同じ名前の人物が登録されています。",
            "person_referenced" => "この人物は参照されているため削除できません。",
            "created_result_deleted" => "作成した人物は削除されています。",
            "revision_conflict" => "ほかの操作で変更されています。最新の内容を確認してください。",
            _ => "人物の操作を完了できませんでした。入力内容を確認してください。"
        }
    };

    private PageLayout PeoplePage(string title, bool backToHome = false)
    {
        var page = new PageLayout(Font);
        SetPage(page);
        if (backToHome)
        {
            var navigation = PageLayout.Actions();
            page.AddSection(navigation);
            AddButton("← ホームへ戻る", () => ShowLedgerHome(openedLedger!), navigation);
        }
        page.AddHeading(title, openedLedger!.Name);
        return page;
    }

    private ListView PeopleList(IEnumerable<PersonInfo> people, Guid? mine)
    {
        var list = new ListView { View = View.Details, FullRowSelect = true, GridLines = false,
            MultiSelect = false, HideSelection = false, AccessibleName = "人物一覧",
            MinimumSize = new Size(0, 120), Margin = new Padding(0, 0, 0, 12) };
        list.Columns.Add("人物名", 160); list.Columns.Add("有効開始日", 125); list.Columns.Add("有効終了日", 125);
        void FitColumns()
        {
            var dateWidth = Math.Max(TextRenderer.MeasureText("2026/12/31", list.Font).Width,
                TextRenderer.MeasureText("有効開始日", list.Font).Width) + list.LogicalToDeviceUnits(20);
            list.Columns[1].Width = list.Columns[2].Width = dateWidth;
            list.Columns[0].Width = Math.Max(list.LogicalToDeviceUnits(100),
                list.ClientSize.Width - dateWidth * 2 - list.LogicalToDeviceUnits(24));
        }
        list.SizeChanged += (_, _) => FitColumns();
        list.FontChanged += (_, _) => FitColumns();
        list.DpiChangedAfterParent += (_, _) => FitColumns();
        AddPeopleRows(list, people, mine); return list;
    }

    private static void AddPeopleRows(ListView list, IEnumerable<PersonInfo> people, Guid? mine)
    {
        foreach (var row in people)
        {
            var item = new ListViewItem(row.Name + (row.PersonId == mine ? "（本人）" : "")) { Tag = row };
            item.SubItems.Add(PersonDate(row.ValidFrom)); item.SubItems.Add(PersonDate(row.ValidTo)); list.Items.Add(item);
        }
    }

    private static PersonInfo? SelectedPerson(ListView list) => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as PersonInfo : null;
    private static string PersonDate(DateOnly? value) => DateTimeInput.FormatDate(value);
}
