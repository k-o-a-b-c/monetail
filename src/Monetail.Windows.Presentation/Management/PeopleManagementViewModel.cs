using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
public enum ManagementEditorOperation
{
    None,
    Create,
    Edit,
    Delete
}

public sealed record ManagementInputError(string? Field, string Message);

/// <summary>人物の表示・編集・順序の唯一の画面状態。保存値と要求IDは既存Clientの型をそのまま使う。</summary>
public sealed partial class PeopleManagementViewModel : ObservableObject, IDisposable
{
    public ManagementContext Context { get; }
    public PeopleInfo? Info { get; private set; }
    public ObservableCollection<PersonRow> Rows { get; } = [];
    public ObservableCollection<PersonRow> OrderRows { get; } = [];

    private readonly HashSet<Guid> missing = [];
    public PersonInfo? Editing { get; private set; }
    public PersonInfo? Latest { get; private set; }
    public PersonCreationDraft? Creation { get; private set; }
    public Guid OrderRevision { get; private set; }
    public bool IncludesPeriod { get; private set; }

    private bool editorDenied, orderDenied;
    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    public partial PersonRow? Selected { get; set; }

    [ObservableProperty]
    public partial PersonRow? SelectedOrder { get; set; }

    public bool IsOrdering => OrderRevision != Guid.Empty;

    [ObservableProperty]
    public partial ManagementEditorOperation Operation { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string FromText { get; set; } = "";

    [ObservableProperty]
    public partial string ToText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(PeriodError), nameof(SaveError))]
    public partial ManagementInputError? InputError { get; private set; }
    public string? NameError => InputError?.Field == "name" ? InputError.Message : null;
    public string? PeriodError => InputError?.Field is "validFrom" or "validTo" ? InputError.Message : null;
    public string? SaveError => InputError is not null && NameError is null && PeriodError is null ? InputError.Message : null;

    [ObservableProperty]
    public partial string? ListError { get; private set; }

    [ObservableProperty]
    public partial string? OrderNotice { get; private set; }

    [ObservableProperty]
    public partial bool OrderNoticeInformational { get; private set; }

    [ObservableProperty]
    public partial bool Restored { get; private set; }

    public PeopleManagementViewModel(ManagementContext context)
    {
        Context = context;
        context.PropertyChanged += ContextChanged;
    }

    public event Func<Task>? EditorRequested;
    public bool IsAdmin => Context.Permissions.CurrentRole == "Admin";
    public bool CanView => Context.Permissions.CurrentRole is not null;
    public bool HasEditor => Operation != ManagementEditorOperation.None;
    public bool IsDeleting => Operation == ManagementEditorOperation.Delete;
    public bool HasLatest => Latest is not null;
    public bool CanRestoreAuthentication => IsOrdering || HasEditor && !IsDeleting;
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyText => Info is null ? "人物一覧を読み込んでいます。" : "表示対象の人物はありません。";
    public string Title => IsDeleting ? "人物の削除" : Editing is null ? "人物の追加" : "人物の編集";
    public string PrimaryText => IsDeleting ? "削除" : Editing is null ? "登録" : "保存";
    public string DeleteNotice => $"「{Editing?.Name}」を削除しますか。参照が残っている人物は削除できません。";
    public string ConflictExplanation => IsDeleting ? "切り替えると、削除対象の確認内容を最新内容に置き換えます。確認してから削除してください。" : "切り替えると、現在の未保存の入力を最新内容に置き換えます。";
    public string LatestName => Latest?.Name ?? "";
    public string LatestFrom => DateTimeInput.FormatDate(Latest?.ValidFrom);
    public string LatestTo => DateTimeInput.FormatDate(Latest?.ValidTo);
    public bool CanSave => Context.CanOperate && HasEditor && !editorDenied && Latest is null && (IsDeleting ? Editing is not null && CanDelete(Editing) : Editing is null ? IsAdmin : CanEdit(Editing) && (!IncludesPeriod || IsAdmin));

    private bool CanEdit(PersonInfo row) => CanView && !missing.Contains(row.PersonId) && (IsAdmin || Context.Permissions.MyPersonId == row.PersonId);
    private bool CanDelete(PersonInfo row) => IsAdmin && CanEdit(row);
    private bool CanAdd() => Context.CanOperate && IsAdmin && !HasEditor && !IsOrdering;
    private bool CanEditSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanEdit(row.Person);
    private bool CanDeleteSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanDelete(row.Person);
    private bool CanOrder() => CanAdd() && Info is not null;
    private bool CanSaveOrder() => Context.CanOperate && IsOrdering && !orderDenied && IsAdmin;
    private bool CanMoveUp() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) > 0;
    private bool CanMoveDown() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) < OrderRows.Count - 1;
    partial void OnIncludeInactiveChanged(bool value) => RenderRows();
    partial void OnSelectedChanged(PersonRow? value) => NotifyState();
    partial void OnSelectedOrderChanged(PersonRow? value) => NotifyState();
    private void ContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ManagementContext.Permissions))
            RenderRows();
        NotifyState();
    }

    public Task LoadAsync(CancellationToken token) => Context.ReadAsync(async () =>
    {
        try
        {
            var result = await Context.People.ListAsync(token);
            if (!token.IsCancellationRequested && !Context.IsDisposed)
                Apply(result);
        }
        catch (PeopleRequestException error)
        {
            token.ThrowIfCancellationRequested();
            ApplyFailure(error);
            throw;
        }
    }, token, message => ListError = message);
    public void Apply(PeopleInfo info)
    {
        if (Context.IsDisposed)
            return;
        Info = info;
        missing.Clear();
        Context.ApplyPermissions(new(info.CurrentRole, info.MyPersonId));
        Context.Masters.UpdatePeople(Context.Ledger.LedgerId, info);
        ListError = null;
        RenderRows();
    }

    private void RenderRows()
    {
        var id = Selected?.Person.PersonId;
        Rows.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var person in (Info?.People ?? []).Where(p => IncludeInactive || (p.ValidFrom is null || p.ValidFrom <= today) && (p.ValidTo is null || p.ValidTo >= today)).OrderByDescending(p => p.PersonId == Context.Permissions.MyPersonId).ThenBy(p => p.DisplayOrder))
            Rows.Add(new(person, person.PersonId == Context.Permissions.MyPersonId));
        Selected = Rows.FirstOrDefault(p => p.Person.PersonId == id);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync() => OpenAsync(null, false);
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private Task EditAsync() => OpenAsync(Selected!.Person, false);
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteAsync() => OpenAsync(Selected!.Person, true);
    private Task OpenAsync(PersonInfo? person, bool deletion)
    {
        Editing = person;
        Latest = null;
        Creation = null;
        editorDenied = false;
        IncludesPeriod = !deletion && IsAdmin;
        Operation = deletion ? ManagementEditorOperation.Delete : person is null ? ManagementEditorOperation.Create : ManagementEditorOperation.Edit;
        Name = person?.Name ?? "";
        FromText = DateTimeInput.FormatDate(person?.ValidFrom);
        ToText = DateTimeInput.FormatDate(person?.ValidTo);
        Restored = false;
        ClearErrors();
        NotifyState();
        return EditorRequested?.Invoke() ?? Task.CompletedTask;
    }

    public async Task<bool> SaveEditorAsync(DateOnly? from, DateOnly? to)
    {
        if (!CanSave)
            return false;
        ClearErrors();
        PersonInput? input = null;
        if (!IsDeleting)
        {
            try
            {
                input = new(Name, IncludesPeriod ? from : Editing?.ValidFrom, IncludesPeriod ? to : Editing?.ValidTo);
            }
            catch (PersonInputException error)
            {
                ShowError(error.Field, error.Message);
                return false;
            }

            if (Editing is null && Creation?.Input != input)
                Creation = new(input);
        }

        var saved = false;
        await Context.RunAsync(async () =>
        {
            try
            {
                var result = IsDeleting ? await Context.People.DeleteAsync(Editing!, Context.Token) : Editing is null ? await Context.People.CreateAsync(Creation!, Context.Token) : await Context.People.UpdateAsync(Editing, input!, IncludesPeriod, Context.Token);
                Apply(result);
                saved = !Context.IsDisposed;
            }
            catch (PeopleRequestException error)
            {
                HandleFailure(error);
            }
        }, message => ShowError(null, message));
        return saved;
    }

    public void ApplyFailure(PeopleRequestException error)
    {
        Context.ApplyPermissions(error.Permissions);
        if (error.StatusCode == HttpStatusCode.NotFound && error.Permissions?.CurrentRole is not null && Editing is not null)
            missing.Add(Editing.PersonId);
        NotifyState();
    }

    private void HandleFailure(PeopleRequestException error)
    {
        if (error.Code == "revision_conflict" && error.LatestPerson is { } latest)
            Latest = latest;
        else
        {
            ApplyFailure(error);
            if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                editorDenied = true;
            ShowError(error.Field, error.StatusCode switch
            {
                HttpStatusCode.Forbidden => IsDeleting ? "この人物を削除する権限がありません。" : "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => IsDeleting ? "この人物は削除されているか、この家計簿に参加していません。" : "この人物を編集できないか、この家計簿に参加していません。入力内容は保存されていません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "person_referenced" => "参照が残っているため削除できません。参加者との対応など、人物の使用箇所を確認してください。",
                    "duplicate_person_name" => "同じ名前の人物が登録されています。",
                    "created_result_deleted" => "作成した人物は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして、追加し直してください。",
                    "invalid_input" when error.Field == "name" => "人物名を1〜100文字で入力してください。",
                    "invalid_input" when error.Field is "validFrom" or "validTo" => "有効開始日は有効終了日以前の日付にしてください。",
                    _ => IsDeleting ? "人物を削除できませんでした。内容を確認してください。" : "人物を保存できませんでした。入力内容を確認してください。"
                }});
        }

        NotifyState();
    }

    [RelayCommand]
    private void AdoptLatest()
    {
        if (!Context.CanOperate || Latest is not { } latest)
            return;
        Editing = latest;
        Name = latest.Name;
        FromText = DateTimeInput.FormatDate(latest.ValidFrom);
        ToText = DateTimeInput.FormatDate(latest.ValidTo);
        Latest = null;
        ClearErrors();
        NotifyState();
    }

    public void CloseEditor()
    {
        Name = FromText = ToText = "";
        Editing = Latest = null;
        Creation = null;
        Operation = ManagementEditorOperation.None;
        editorDenied = false;
        ClearErrors();
        NotifyState();
    }

    public void ClearErrors() => InputError = null;
    public void ClearListError() => ListError = null;
    public void ClearOrderNotice() => OrderNotice = null;
    public void ShowError(string? field, string message) => InputError = new(field, message);

    [RelayCommand(CanExecute = nameof(CanOrder))]
    private void Order() => BeginOrder(Info!.People.OrderBy(p => p.DisplayOrder), Info.OrderRevision);
    private void BeginOrder(IEnumerable<PersonInfo> people, Guid revision)
    {
        OrderRevision = revision;
        orderDenied = false;
        OrderNotice = null;
        Restored = false;
        OrderRows.Clear();
        foreach (var row in people)
            OrderRows.Add(new(row, row.PersonId == Context.Permissions.MyPersonId));
        SelectedOrder = null;
        NotifyState();
        OnPropertyChanged(nameof(IsOrdering));
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);
    private void Move(int delta)
    {
        if (!CanSaveOrder() || SelectedOrder is not { } row)
            return;
        var index = OrderRows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= OrderRows.Count)
            return;
        OrderRows.Move(index, target);
        SelectedOrder = row;
        OnPropertyChanged(nameof(SelectedOrder));
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanSaveOrder))]
    private async Task SaveOrderAsync()
    {
        var ids = OrderRows.Select(p => p.Person.PersonId).ToArray();
        var revision = OrderRevision;
        OrderNotice = null;
        await Context.RunAsync(async () =>
        {
            try
            {
                Apply(await Context.People.SaveOrderAsync(revision, ids, Context.Token));
                FinishOrder();
            }
            catch (PeopleRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestPeople is { } latest)
                {
                    Apply(latest);
                    BeginOrder(latest.People.OrderBy(p => p.DisplayOrder), latest.OrderRevision);
                    OrderNoticeInformational = true;
                    OrderNotice = "ほかの操作で変更されています。最新の並び順を読み込みます。";
                }
                else
                {
                    ApplyFailure(error);
                    orderDenied = error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                    OrderNoticeInformational = false;
                    OrderNotice = error.StatusCode switch
                    {
                        HttpStatusCode.Forbidden => "並び順を変更する権限がありません。変更内容は保存されていません。",
                        HttpStatusCode.NotFound => "この家計簿を操作できません。変更内容は保存されていません。",
                        HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                        _ => "並び順を保存できませんでした。内容を確認してください。"
                    };
                }
            }
        }, message =>
        {
            OrderNoticeInformational = false;
            OrderNotice = message;
        });
        NotifyState();
    }

    [RelayCommand]
    private void CancelOrder()
    {
        if (Context.IsBusy)
            return;
        FinishOrder();
    }

    private void FinishOrder()
    {
        OrderRevision = Guid.Empty;
        OrderRows.Clear();
        SelectedOrder = null;
        OrderNotice = null;
        orderDenied = false;
        NotifyState();
        OnPropertyChanged(nameof(IsOrdering));
    }

    public void Leave()
    {
        CloseEditor();
        FinishOrder();
    }

    public async Task ResumeAsync(string? denied, CancellationToken token)
    {
        if (denied is null)
        {
            try
            {
                Apply(await Context.People.ListAsync(token));
            }
            catch (PeopleRequestException error)when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                ApplyFailure(error);
                denied = "現在の権限ではこの編集を保存できません。未保存の入力は残しています。";
            }
        }

        Restored = true;
        if (IsOrdering)
        {
            var selectedId = SelectedOrder?.Person.PersonId;
            var latest = (Info?.People ?? []).ToDictionary(p => p.PersonId);
            for (var i = 0; i < OrderRows.Count; i++)
            {
                var previous = OrderRows[i].Person;
                OrderRows[i] = new(latest.GetValueOrDefault(previous.PersonId) ?? previous, previous.PersonId == Context.Permissions.MyPersonId);
            }

            SelectedOrder = OrderRows.FirstOrDefault(row => row.Person.PersonId == selectedId);
            orderDenied = denied is not null || !IsAdmin;
            OrderNoticeInformational = false;
            OrderNotice = orderDenied ? denied ?? "現在の権限では並び順を保存できません。未保存の並び順は残しています。" : null;
        }
        else if (HasEditor)
        {
            if (Editing is not null && Info?.People.Any(p => p.PersonId == Editing.PersonId) != true)
                missing.Add(Editing.PersonId);
            editorDenied = denied is not null || IncludesPeriod && !IsAdmin || (Editing is null ? !IsAdmin : !CanEdit(Editing));
            if (editorDenied)
                ShowError(null, denied ?? "現在の権限ではこの編集を保存できません。未保存の入力は残しています。");
        }

        NotifyState();
    }

    private void NotifyState()
    {
        foreach (var name in new[]
        {
            nameof(IsAdmin),
            nameof(CanView),
            nameof(HasEditor),
            nameof(IsDeleting),
            nameof(HasLatest),
            nameof(CanSave),
            nameof(IncludesPeriod),
            nameof(Title),
            nameof(PrimaryText),
            nameof(DeleteNotice),
            nameof(ConflictExplanation),
            nameof(LatestName),
            nameof(LatestFrom),
            nameof(LatestTo),
            nameof(CanRestoreAuthentication)
        }

        )
            OnPropertyChanged(name);
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OrderCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SaveOrderCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Context.PropertyChanged -= ContextChanged;
        EditorRequested = null;
    }
}
