using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    private PeopleInfo? peopleInfo;
    private PeoplePermissions? peoplePermissions;
    private PersonInfo? editingPerson;
    private PersonInfo? latestPerson;
    private PersonCreationDraft? personDraft;
    private readonly HashSet<Guid> missingPeople = [];
    private bool editingPeriod;
    private bool isDeletingPerson;
    private bool editorDenied;
    private FormKeyboard personKeyboard = null!;
    private PersonEditSnapshot? pendingPersonEdit;
    private Task personDialogTask = Task.CompletedTask;
    private bool personDialogOpen;
    private bool personDialogClosing;
    private bool forcePersonDialogClose;

    private sealed record PersonEditSnapshot(Guid AccountId, string ApiOrigin, LedgerInfo Ledger, PeopleInfo People,
        PersonInfo? Person, PersonCreationDraft? Creation, bool IncludedPeriod, string Name, string From, string To,
        PersonOrderDraft? Order = null, bool IncludeInactive = false);

    private bool IsPeopleAdmin => peoplePermissions?.CurrentRole == "Admin";
    private bool CanViewPeople => peoplePermissions is null || peoplePermissions.CurrentRole is not null;
    private bool CanEditPerson(PersonInfo person) => account is not null && CanViewPeople &&
        !missingPeople.Contains(person.PersonId) && (IsPeopleAdmin || peoplePermissions?.MyPersonId == person.PersonId);

    private bool CanDeletePerson(PersonInfo person) => IsPeopleAdmin && CanEditPerson(person);

    private void InitializeInput()
    {
        // 本体ごと標準Popupへ配置し、本文と標準ボタンを同じTab・キー処理範囲にする。
        InitializePayments();
        InitializeCategories();
        WorkspaceRoot.Children.Remove(PersonDialog);
        WorkspaceRoot.Children.Remove(ParticipantDialog);
        WorkspaceRoot.Children.Remove(InvitationCancelDialog);
        _ = FormKeyboard.ForDialog(ParticipantDialog, async () =>
        {
            if (!excludingMember && await TrySaveParticipantAsync()) ParticipantDialog.Hide();
        });
        PersonValidFrom.IsCancellationTarget = PersonValidTo.IsCancellationTarget =
            focused => focused is Button button && Equals(button.Content, PersonDialog.CloseButtonText);
        personKeyboard = FormKeyboard.ForDialog(PersonDialog, SavePersonAsync, PersonName, PersonValidFrom, PersonValidTo);
        _ = new FormKeyboard(PersonOrderContent, SavePersonOrderButton, SavePersonOrderAsync);
        _ = new FormKeyboard(ConnectionPage, null, null, ServerUrl);
        _ = new FormKeyboard(ConnectionSettingsContent, null, null, SettingsServerUrl);
        _ = new FormKeyboard(LedgerCreatePage, CreateLedgerButton, CreateLedgerAsync, NewLedgerName);
    }

    private void ResetPeople()
    {
        DiscardPersonEditor();
        DiscardPersonOrder();
        peopleInfo = null;
        IncludeInactivePeople.IsChecked = false;
        peoplePermissions = null;
        missingPeople.Clear();
        PeopleList.ItemsSource = null;
        EmptyPeople.Text = "人物一覧を読み込んでいます。";
        EmptyPeople.Visibility = Visibility.Visible;
        UpdatePeopleActions();
    }

    private void UpdatePeopleActions()
    {
        UpdatePaymentActions();
        UpdateCategoryActions();
        UpdateParticipantActions();
        AddPersonButton.IsEnabled = account is not null && IsPeopleAdmin;
        PersonOrderButton.IsEnabled = account is not null && IsPeopleAdmin && peopleInfo is not null;
        UpdatePersonOrderActions();
        EditPersonButton.IsEnabled = PeopleList.SelectedItem is PersonRow row && CanEditPerson(row.Person);
        DeletePersonButton.IsEnabled = PeopleList.SelectedItem is PersonRow selected && CanDeletePerson(selected.Person);
        PersonDialog.IsPrimaryButtonEnabled = !busy && PersonEditorContent.Visibility == Visibility.Visible && !editorDenied && latestPerson is null && account is not null &&
            (isDeletingPerson ? editingPerson is not null && CanDeletePerson(editingPerson) :
                editingPerson is null ? IsPeopleAdmin : CanEditPerson(editingPerson) && (!editingPeriod || IsPeopleAdmin));
        if (PersonEditorContent.Visibility != Visibility.Visible) PeopleNavigation.IsEnabled = CanViewPeople;
    }

    private void SelectPerson(object sender, SelectionChangedEventArgs args) => UpdatePeopleActions();

    private void AddPerson(object sender, RoutedEventArgs args)
    {
        if (busy || account is null || !IsPeopleAdmin) return;
        OpenPersonEditor(null);
    }

    private void EditPerson(object sender, RoutedEventArgs args)
    {
        if (busy || PeopleList.SelectedItem is not PersonRow row || !CanEditPerson(row.Person)) return;
        OpenPersonEditor(row.Person);
    }

    private void DeletePerson(object sender, RoutedEventArgs args)
    {
        if (busy || PeopleList.SelectedItem is not PersonRow row || !CanDeletePerson(row.Person)) return;
        OpenPersonEditor(row.Person, deletion: true);
    }

    private void OpenPersonEditor(PersonInfo? person, bool deletion = false)
    {
        if (ManagementDialogOpen || IsOrderingPeople) return;
        editingPerson = person;
        isDeletingPerson = deletion;
        editingPeriod = !deletion && IsPeopleAdmin;
        latestPerson = null;
        personDraft = null;
        editorDenied = false;
        PersonDialog.Title = deletion ? "人物の削除" : person is null ? "人物の追加" : "人物の編集";
        PersonDialog.PrimaryButtonText = deletion ? "削除" : person is null ? "登録" : "保存";
        PersonDialog.DefaultButton = deletion ? ContentDialogButton.Close : ContentDialogButton.None;
        PersonFields.Visibility = deletion ? Visibility.Collapsed : Visibility.Visible;
        PersonDeleteNotice.Visibility = deletion ? Visibility.Visible : Visibility.Collapsed;
        UpdatePersonDeleteNotice();
        PersonConflictExplanation.Text = deletion
            ? "切り替えると、削除対象の確認内容を最新内容に置き換えます。確認してから削除してください。"
            : "切り替えると、現在の未保存の入力を最新内容に置き換えます。";
        PersonName.Text = person?.Name ?? "";
        PersonValidFrom.Text = DateTimeInput.FormatDate(person?.ValidFrom);
        PersonValidTo.Text = DateTimeInput.FormatDate(person?.ValidTo);
        PersonValidFrom.IsReadOnly = PersonValidTo.IsReadOnly = !editingPeriod;
        PersonValidFrom.IsTabStop = PersonValidTo.IsTabStop = editingPeriod;
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = false;
        PersonPeriodPermission.Visibility = editingPeriod ? Visibility.Collapsed : Visibility.Visible;
        PersonConflict.Visibility = Visibility.Collapsed;
        PersonRestoredNotice.Visibility = Visibility.Collapsed;
        ClearPersonErrors();
        ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        PeopleContent.Visibility = Visibility.Visible;
        PersonEditorContent.Visibility = Visibility.Visible;
        UpdatePeopleActions();
        personDialogTask = ShowPersonDialogAsync();
    }

    private async Task ShowPersonDialogAsync()
    {
        personDialogOpen = true;
        personDialogClosing = forcePersonDialogClose = false;
        PersonDialog.XamlRoot = WorkspaceRoot.XamlRoot;
        try { await PersonDialog.ShowAsync(); }
        finally
        {
            personDialogOpen = false;
            personDialogClosing = false;
            if (!closed)
            {
                ClearPersonEditor();
                if (!forcePersonDialogClose && !CanViewPeople) ShowPage(HomePage);
            }
        }
    }

    private void PersonDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (!isDeletingPerson) FocusPersonField(PersonName);
    }

    private void UpdatePersonDeleteNotice() => PersonDeleteNotice.Text =
        $"「{editingPerson?.Name}」を削除しますか。参照が残っている人物は削除できません。";

    private void PersonDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (busy && !forcePersonDialogClose && !closed) { args.Cancel = true; return; }
        personDialogClosing = true;
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = true;
    }

    private async void SavePerson(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try { args.Cancel = !await TrySavePersonAsync(); }
        finally { deferral.Complete(); }
    }

    private async Task SavePersonAsync()
    {
        // Ctrl+Enterは登録・保存だけ。削除は確認ダイアログの明示操作で行う。
        if (!isDeletingPerson && await TrySavePersonAsync()) PersonDialog.Hide();
    }

    private async Task<bool> TrySavePersonAsync()
    {
        if (busy || personKeyboard.IsComposing || !PersonDialog.IsPrimaryButtonEnabled) return false;
        if (isDeletingPerson) return await TryDeletePersonAsync();
        var saved = false;
        ClearPersonErrors();
        PersonInput input;
        try
        {
            var from = editingPeriod ? PersonValidFrom.Complete() : editingPerson?.ValidFrom;
            var to = editingPeriod ? PersonValidTo.Complete() : editingPerson?.ValidTo;
            input = new PersonInput(PersonName.Text, from, to);
        }
        catch (PersonInputException error) { ShowPersonError(error.Field, error.Message); return false; }
        if (editingPerson is null && personDraft?.Input != input) personDraft = new(input);
        await RunAsync(async () =>
        {
            try
            {
                var workspace = new PeopleWorkspace(connection.Api, connection.Session, currentLedger!.LedgerId);
                var result = editingPerson is null
                    ? await workspace.CreateAsync(personDraft!, lifetime.Token)
                    : await workspace.UpdateAsync(editingPerson, input, editingPeriod, lifetime.Token);
                DisplayPeople(result);
                saved = true;
            }
            catch (PeopleRequestException error) { HandlePersonFailure(error); }
        }, SavePersonAsync);
        return saved;
    }

    private async Task<bool> TryDeletePersonAsync()
    {
        ClearPersonErrors();
        var deleted = false;
        await RunAsync(async () =>
        {
            try
            {
                var workspace = new PeopleWorkspace(connection.Api, connection.Session, currentLedger!.LedgerId);
                DisplayPeople(await workspace.DeleteAsync(editingPerson!, lifetime.Token));
                deleted = true;
            }
            catch (PeopleRequestException error) { HandlePersonFailure(error); }
        }, () => Task.CompletedTask);
        return deleted;
    }

    private void HandlePersonFailure(PeopleRequestException error)
    {
        if (error.Code == "revision_conflict" && error.LatestPerson is { } latest)
        {
            latestPerson = latest;
            LatestPersonName.Text = latest.Name;
            LatestPersonFrom.Text = DateTimeInput.FormatDate(latest.ValidFrom);
            LatestPersonTo.Text = DateTimeInput.FormatDate(latest.ValidTo);
            PersonConflict.Visibility = Visibility.Visible;
        }
        else
        {
            ApplyPeopleFailureContext(error);
            if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) editorDenied = true;
            var message = error.StatusCode switch
            {
                HttpStatusCode.Forbidden => isDeletingPerson ? "この人物を削除する権限がありません。" :
                    "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => isDeletingPerson ? "この人物は削除されているか、この家計簿に参加していません。" :
                    "この人物を編集できないか、この家計簿に参加していません。入力内容は保存されていません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "person_referenced" => "参照が残っているため削除できません。参加者との対応など、人物の使用箇所を確認してください。",
                    "duplicate_person_name" => "同じ名前の人物が登録されています。",
                    "created_result_deleted" => "作成した人物は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして、追加し直してください。",
                    "invalid_input" when error.Field == "name" => "人物名を1〜100文字で入力してください。",
                    "invalid_input" when error.Field is "validFrom" or "validTo" => "有効開始日は有効終了日以前の日付にしてください。",
                    _ => isDeletingPerson ? "人物を削除できませんでした。内容を確認してください。" :
                        "人物を保存できませんでした。入力内容を確認してください。"
                }
            };
            ShowPersonError(error.Field, message);
        }
        UpdatePeopleActions();
    }

    private void ApplyPeopleFailureContext(PeopleRequestException error)
    {
        if (error.Permissions is not { } permission) return;
        peoplePermissions = permission;
        RenderPeopleRows();
        if (error.StatusCode == HttpStatusCode.NotFound && permission.CurrentRole is not null && editingPerson is not null)
            missingPeople.Add(editingPerson.PersonId);
        UpdatePeopleActions();
    }

    private void UseLatestPerson(object sender, RoutedEventArgs args)
    {
        if (busy || latestPerson is null) return;
        editingPerson = latestPerson;
        if (isDeletingPerson) UpdatePersonDeleteNotice();
        PersonName.Text = latestPerson.Name;
        PersonValidFrom.Text = DateTimeInput.FormatDate(latestPerson.ValidFrom);
        PersonValidTo.Text = DateTimeInput.FormatDate(latestPerson.ValidTo);
        latestPerson = null;
        PersonConflict.Visibility = Visibility.Collapsed;
        ClearPersonErrors();
        UpdatePeopleActions();
        FocusPersonField(PersonName);
    }

    private void DiscardPersonEditor()
    {
        forcePersonDialogClose = true;
        if (personDialogOpen) PersonDialog.Hide();
        ClearPersonEditor();
    }

    private void ClearPersonEditor()
    {
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = true;
        PersonEditorContent.Visibility = Visibility.Collapsed;
        editingPerson = latestPerson = null;
        personDraft = null;
        editorDenied = false;
        isDeletingPerson = false;
        ClearPersonErrors();
        UpdatePeopleActions();
    }

    private void RememberPersonEdit()
    {
        if (account is null || currentLedger is null || peopleInfo is null) return;
        if (orderedPeople is not null)
            pendingPersonEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, peopleInfo,
                null, null, false, "", "", "", new(personOrderRevision, orderedPeople.ToArray()), IncludeInactivePeople.IsChecked == true);
        else if (!isDeletingPerson && PersonEditorContent.Visibility == Visibility.Visible)
            pendingPersonEdit = new(account.AuthUserId, connection.Endpoint!.Value, currentLedger, peopleInfo, editingPerson,
                personDraft, editingPeriod, PersonName.Text, PersonValidFrom.Text, PersonValidTo.Text, IncludeInactive: IncludeInactivePeople.IsChecked == true);
    }

    private void DropPendingPersonEdit()
    {
        if (pendingPersonEdit is null) return;
        pendingPersonEdit = null;
        PersonName.Text = PersonValidFrom.Text = PersonValidTo.Text = "";
    }

    private async Task<bool> ResumePersonEditAsync(CancellationToken token)
    {
        if (pendingPersonEdit is not { } draft || account is null) return false;
        if (draft.AccountId != account.AuthUserId || draft.ApiOrigin != connection.Endpoint!.Value)
        {
            DropPendingPersonEdit();
            return false;
        }
        await personDialogTask;
        LedgerInfo opened;
        try { opened = await connection.Ledgers.OpenAsync(draft.Ledger.LedgerId, token); }
        catch (LedgerRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // 既に本人へ返した入力だけを保持する。未参加の家計簿の最新情報は取得しない。
            RestorePersonEditor(draft, draft.Ledger, draft.People, new(null, null),
                "この家計簿を開く権限がありません。未保存の入力は残していますが、保存できません。");
            return true;
        }
        try
        {
            var people = await new PeopleWorkspace(connection.Api, connection.Session, opened.LedgerId).ListAsync(token);
            RestorePersonEditor(draft, opened, people);
        }
        catch (PeopleRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            RestorePersonEditor(draft, opened, draft.People, error.Permissions ?? new(null, null),
                "現在の権限ではこの編集を保存できません。未保存の入力は残しています。");
        }
        return true;
    }

    private void RestorePersonEditor(PersonEditSnapshot draft, LedgerInfo ledger, PeopleInfo people,
        PeoplePermissions? permission = null, string? failure = null)
    {
        DisplayLedger(ledger);
        DisplayPeople(people);
        IncludeInactivePeople.IsChecked = draft.IncludeInactive;
        ShowPage(ManagementPage);
        ManagementNavigation.SelectedItem = PeopleNavigation;
        if (permission is not null) peoplePermissions = permission;
        if (draft.Order is { } order) RestorePersonOrder(order, people, failure);
        else
        {
            OpenPersonEditor(draft.Person);
            PersonName.Text = draft.Name;
            PersonValidFrom.Text = draft.From;
            PersonValidTo.Text = draft.To;
            personDraft = draft.Creation;
            if (draft.Person is not null && !people.People.Any(person => person.PersonId == draft.Person.PersonId))
                missingPeople.Add(draft.Person.PersonId);
            // 古い版と作成要求IDを維持し、再ログインを理由に上書き・重複作成しない。
            editorDenied = failure is not null || draft.IncludedPeriod && !IsPeopleAdmin ||
                (draft.Person is null ? !IsPeopleAdmin : !CanEditPerson(draft.Person));
            PersonRestoredNotice.Visibility = Visibility.Visible;
            if (editorDenied) ShowPersonError(null, failure ?? "現在の権限ではこの編集を保存できません。未保存の入力は残しています。");
        }
        pendingPersonEdit = null;
        RenderPeopleRows();
        UpdatePeopleActions();
    }

    private void ClearPersonErrors()
    {
        PersonNameError.Visibility = PersonPeriodError.Visibility = PersonSaveError.Visibility = Visibility.Collapsed;
    }

    private void ShowPersonError(string? field, string message)
    {
        var text = field == "name" ? PersonNameError : field is "validFrom" or "validTo" ? PersonPeriodError : PersonSaveError;
        text.Text = message;
        text.Visibility = Visibility.Visible;
        if (field is not null)
            FocusPersonField(field == "name" ? PersonName : field == "validFrom" ? PersonValidFrom : PersonValidTo);
    }

    private void FocusPersonField(Control control) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!closed && !isDeletingPerson && PersonEditorContent.Visibility == Visibility.Visible) control.Focus(FocusState.Programmatic);
    });
}
