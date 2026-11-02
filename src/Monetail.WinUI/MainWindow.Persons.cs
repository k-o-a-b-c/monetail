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
    private bool editorDenied;
    private FormKeyboard personKeyboard = null!;

    private bool IsPeopleAdmin => peoplePermissions?.CurrentRole == "Admin";
    private bool CanViewPeople => peoplePermissions is null || peoplePermissions.CurrentRole is not null;
    private bool CanEditPerson(PersonInfo person) => account is not null && CanViewPeople &&
        !missingPeople.Contains(person.PersonId) && (IsPeopleAdmin || peoplePermissions?.MyPersonId == person.PersonId);

    private void InitializeInput()
    {
        PersonValidFrom.CancellationTarget = PersonValidTo.CancellationTarget = CancelPersonButton;
        personKeyboard = new(PersonEditorContent, SavePersonButton, SavePersonAsync, PersonName, PersonValidFrom, PersonValidTo);
        _ = new FormKeyboard(ConnectionPage, null, null, ServerUrl);
        _ = new FormKeyboard(ConnectionSettingsContent, null, null, SettingsServerUrl);
        _ = new FormKeyboard(LedgerCreatePage, CreateLedgerButton, CreateLedgerAsync, NewLedgerName);
    }

    private void ResetPeople()
    {
        DiscardPersonEditor();
        peopleInfo = null;
        peoplePermissions = null;
        missingPeople.Clear();
        PeopleList.ItemsSource = null;
        EmptyPeople.Text = "人物一覧を読み込んでいます。";
        EmptyPeople.Visibility = Visibility.Visible;
        UpdatePeopleActions();
    }

    private void UpdatePeopleActions()
    {
        AddPersonButton.IsEnabled = account is not null && IsPeopleAdmin;
        EditPersonButton.IsEnabled = PeopleList.SelectedItem is PersonRow row && CanEditPerson(row.Person);
        SavePersonButton.IsEnabled = PersonEditorContent.Visibility == Visibility.Visible && !editorDenied && latestPerson is null && account is not null &&
            (editingPerson is null ? IsPeopleAdmin : CanEditPerson(editingPerson) && (!editingPeriod || IsPeopleAdmin));
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

    private void OpenPersonEditor(PersonInfo? person)
    {
        editingPerson = person;
        editingPeriod = IsPeopleAdmin;
        latestPerson = null;
        personDraft = null;
        editorDenied = false;
        PersonEditorTitle.Text = person is null ? "人物の追加" : "人物の編集";
        SavePersonButton.Content = person is null ? "登録" : "保存";
        PersonName.Text = person?.Name ?? "";
        PersonValidFrom.Text = DateTimeInput.FormatDate(person?.ValidFrom);
        PersonValidTo.Text = DateTimeInput.FormatDate(person?.ValidTo);
        PersonValidFrom.IsReadOnly = PersonValidTo.IsReadOnly = !editingPeriod;
        PersonValidFrom.IsTabStop = PersonValidTo.IsTabStop = editingPeriod;
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = false;
        PersonPeriodPermission.Visibility = editingPeriod ? Visibility.Collapsed : Visibility.Visible;
        PersonConflict.Visibility = Visibility.Collapsed;
        ClearPersonErrors();
        PeopleContent.Visibility = ConnectionSettingsContent.Visibility = AccountContent.Visibility = Visibility.Collapsed;
        PersonEditorContent.Visibility = Visibility.Visible;
        UpdatePeopleActions();
        FocusPersonField(PersonName);
    }

    private async void SavePerson(object sender, RoutedEventArgs args) => await SavePersonAsync();

    private async Task SavePersonAsync()
    {
        if (busy || personKeyboard.IsComposing || !SavePersonButton.IsEnabled) return;
        ClearPersonErrors();
        PersonInput input;
        try
        {
            var from = editingPeriod ? PersonValidFrom.Complete() : editingPerson?.ValidFrom;
            var to = editingPeriod ? PersonValidTo.Complete() : editingPerson?.ValidTo;
            input = new PersonInput(PersonName.Text, from, to);
        }
        catch (PersonInputException error) { ShowPersonError(error.Field, error.Message); return; }
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
                DiscardPersonEditor();
                PeopleContent.Visibility = Visibility.Visible;
                UpdatePeopleActions();
            }
            catch (PeopleRequestException error) { HandlePersonFailure(error); }
        }, SavePersonAsync);
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
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => "この人物を編集できないか、この家計簿に参加していません。入力内容は保存されていません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "duplicate_person_name" => "同じ名前の人物が登録されています。",
                    "created_result_deleted" => "作成した人物は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして、追加し直してください。",
                    "invalid_input" when error.Field == "name" => "人物名を1〜100文字で入力してください。",
                    "invalid_input" when error.Field is "validFrom" or "validTo" => "有効開始日は有効終了日以前の日付にしてください。",
                    _ => "人物を保存できませんでした。入力内容を確認してください。"
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
        PersonName.Text = latestPerson.Name;
        PersonValidFrom.Text = DateTimeInput.FormatDate(latestPerson.ValidFrom);
        PersonValidTo.Text = DateTimeInput.FormatDate(latestPerson.ValidTo);
        latestPerson = null;
        PersonConflict.Visibility = Visibility.Collapsed;
        ClearPersonErrors();
        UpdatePeopleActions();
        FocusPersonField(PersonName);
    }

    private void CancelPerson(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        DiscardPersonEditor();
        if (!CanViewPeople) { ShowPage(HomePage); return; }
        PeopleContent.Visibility = Visibility.Visible;
        UpdatePeopleActions();
    }

    private void DiscardPersonEditor()
    {
        PersonValidFrom.SuppressCompletion = PersonValidTo.SuppressCompletion = true;
        PersonEditorContent.Visibility = Visibility.Collapsed;
        editingPerson = latestPerson = null;
        personDraft = null;
        editorDenied = false;
        ClearPersonErrors();
        ErrorNotice.IsOpen = false;
        ErrorNotice.Visibility = Visibility.Collapsed;
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
        if (!closed && PersonEditorContent.Visibility == Visibility.Visible) control.Focus(FocusState.Programmatic);
    });
}
