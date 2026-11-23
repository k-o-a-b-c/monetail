using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
/// <summary>科目の表示・編集・順序の唯一の画面状態。保存値と要求IDは既存Clientの型をそのまま使う。</summary>
public sealed partial class CategoriesManagementViewModel : ObservableObject, IDisposable
{
    public ManagementContext Context { get; }
    public CategoriesInfo? Info { get; private set; }
    public ObservableCollection<CategoryRow> Rows { get; } = [];
    public ObservableCollection<CategoryRow> OrderRows { get; } = [];

    private readonly HashSet<Guid> missing = [];
    public CategoryInfo? Editing { get; private set; }
    public CategoryInfo? Latest { get; private set; }
    public CategoryCreationDraft? Creation { get; private set; }
    public Guid OrderRevision { get; private set; }
    private Guid[] originalOrder = [];

    private bool editorDenied, orderDenied;
    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    public partial CategoryRow? Selected { get; set; }

    [ObservableProperty]
    public partial CategoryRow? SelectedOrder { get; set; }

    public bool IsOrdering => OrderRevision != Guid.Empty;
    public bool HasOrderChanges => IsOrdering && !OrderRows.Select(row => row.Category.CategoryId).SequenceEqual(originalOrder);

    [ObservableProperty]
    public partial ManagementEditorOperation Operation { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Income { get; set; } = "Expense";

    [ObservableProperty]
    public partial string Tax { get; set; } = "Standard";


    [ObservableProperty]
    public partial string FromText { get; set; } = "";

    [ObservableProperty]
    public partial string ToText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(PeriodError), nameof(SaveError), nameof(ChoiceError))]
    public partial ManagementInputError? InputError { get; private set; }
    public string? NameError => InputError?.Field == "name" ? InputError.Message : null;
    public string? PeriodError => InputError?.Field is "validFrom" or "validTo" ? InputError.Message : null;
    public string? ChoiceError => InputError?.Field is "incomeExpense" or "taxCategoryCode" ? InputError.Message : null;
    public string? SaveError => InputError is not null && NameError is null && PeriodError is null && ChoiceError is null ? InputError.Message : null;

    [ObservableProperty]
    public partial string? ListError { get; private set; }

    [ObservableProperty]
    public partial string? OrderNotice { get; private set; }

    [ObservableProperty]
    public partial bool OrderNoticeInformational { get; private set; }

    [ObservableProperty]
    public partial bool Restored { get; private set; }

    public CategoriesManagementViewModel(ManagementContext context)
    {
        Context = context;
        context.PropertyChanged += ContextChanged;
    }

    public event Func<Task>? EditorRequested;
    public bool IsAdmin => Info is not null && permission?.CurrentRole == "Admin";
    public bool CanView => permission is null || permission.CurrentRole is not null;

    private PeoplePermissions? permission;
    public bool HasEditor => Operation != ManagementEditorOperation.None;
    public bool IsDeleting => Operation == ManagementEditorOperation.Delete;
    public bool HasLatest => Latest is not null;
    public bool CanRestoreAuthentication => IsOrdering || HasEditor && !IsDeleting;
    public bool IsEmpty => Rows.Count == 0;

    public string Title => IsDeleting ? "科目の削除" : Editing is null ? "科目の追加" : "科目の編集";
    public string PrimaryText => IsDeleting ? "削除" : Editing is null ? "登録" : "保存";
    public string DeleteNotice => $"「{Editing?.Name}」を削除しますか。参照がある科目は削除できません。";

    public string LatestName => Latest?.Name ?? "";
    public string LatestIncome => CategoryRow.IncomeLabel(Latest?.IncomeExpense ?? "");
    public string LatestTax => CategoryRow.TaxLabel(Latest?.TaxCategoryCode ?? "");
    public string LatestFrom => DateTimeInput.FormatDate(Latest?.ValidFrom);
    public string LatestTo => DateTimeInput.FormatDate(Latest?.ValidTo);
    public bool CanSave => Context.CanOperate && HasEditor && !editorDenied && Latest is null && (IsDeleting ? Editing is not null && CanDelete(Editing) : Editing is null ? IsAdmin : CanEdit(Editing));

    private bool CanEdit(CategoryInfo row) => IsAdmin && !missing.Contains(row.CategoryId);
    private bool CanDelete(CategoryInfo row) => IsAdmin && CanEdit(row);
    private bool CanAdd() => Context.CanOperate && IsAdmin && !HasEditor && !IsOrdering;
    private bool CanEditSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanEdit(row.Category);
    private bool CanDeleteSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanDelete(row.Category);
    private bool CanOrder() => CanAdd() && Info is not null;
    private bool CanSaveOrder() => Context.CanOperate && IsOrdering && !orderDenied && IsAdmin;
    private bool CanMoveUp() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) > 0;
    private bool CanMoveDown() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) < OrderRows.Count - 1;
    partial void OnIncludeInactiveChanged(bool value) => RenderRows();
    partial void OnSelectedChanged(CategoryRow? value) => NotifyState();
    partial void OnSelectedOrderChanged(CategoryRow? value) => NotifyState();
    private void ContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        NotifyState();
    }

    public Task LoadAsync(CancellationToken token) => Context.ReadAsync(async () =>
    {
        try
        {
            var result = await Context.Categories.ListAsync(token);
            if (!token.IsCancellationRequested && !Context.IsDisposed)
                Apply(result);
        }
        catch (CategoryRequestException error)
        {
            token.ThrowIfCancellationRequested();
            ApplyFailure(error);
            throw;
        }
    }, token, message => ListError = message);
    public void Apply(CategoriesInfo info)
    {
        if (Context.IsDisposed)
            return;
        Info = info;
        missing.Clear();
        permission = new(info.CurrentRole, null);
        Context.Masters.UpdateCategories(Context.Ledger.LedgerId, info);
        ListError = null;
        RenderRows();
    }

    private void RenderRows()
    {
        var id = Selected?.Category.CategoryId;
        Rows.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var person in (Info?.Categories ?? []).Where(p => IncludeInactive || (p.ValidFrom is null || p.ValidFrom <= today) && (p.ValidTo is null || p.ValidTo >= today)).OrderBy(p => p.DisplayOrder))
            Rows.Add(new(person));
        Selected = Rows.FirstOrDefault(p => p.Category.CategoryId == id);
        OnPropertyChanged(nameof(IsEmpty));
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync() => OpenAsync(null, false);
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private Task EditAsync() => OpenAsync(Selected!.Category, false);
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteAsync() => OpenAsync(Selected!.Category, true);
    private async Task OpenAsync(CategoryInfo? person, bool deletion)
    {
        Editing = person;
        Latest = null;
        Creation = null;
        editorDenied = false;
        Income = person?.IncomeExpense ?? "Expense";
        Tax = person?.TaxCategoryCode ?? "Standard";
        Operation = deletion ? ManagementEditorOperation.Delete : person is null ? ManagementEditorOperation.Create : ManagementEditorOperation.Edit;
        Name = person?.Name ?? "";
        FromText = DateTimeInput.FormatDate(person?.ValidFrom);
        ToText = DateTimeInput.FormatDate(person?.ValidTo);
        Restored = false;
        ClearErrors();
        NotifyState();
        await (EditorRequested?.Invoke() ?? Task.CompletedTask);
    }

    public async Task<bool> SaveEditorAsync(DateOnly? from, DateOnly? to)
    {
        if (!CanSave)
            return false;
        ClearErrors();
        CategoryInput? input = null;
        if (!IsDeleting)
        {
            try
            {
                input = new(Name, Income, Tax, from, to);
            }
            catch (CategoryInputException error)
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
                var result = IsDeleting ? await Context.Categories.DeleteAsync(Editing!, Context.Token) : Editing is null ? (await Context.Categories.CreateAsync(Creation!, Context.Token)).State : await Context.Categories.UpdateAsync(Editing, new(input!.Name, input.IncomeExpense, input.TaxCategoryCode, new(input.ValidFrom), new(input.ValidTo)), Context.Token);
                Apply(result);
                saved = !Context.IsDisposed;
            }
            catch (CategoryRequestException error)
            {
                HandleFailure(error);
            }
        }, message => ShowError(null, message));
        return saved;
    }

    public void ApplyFailure(CategoryRequestException error)
    {
        if (error.Permissions is { } current)
            permission = current;
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            permission = new(null, null);
        if (error.StatusCode == HttpStatusCode.NotFound && Editing is not null)
            missing.Add(Editing.CategoryId);
        NotifyState();
    }

    private void HandleFailure(CategoryRequestException error)
    {
        var row = error.LatestCategory ?? error.LatestCategories?.Categories.FirstOrDefault(row => row.CategoryId == Editing?.CategoryId);
        if (error.Code == "revision_conflict" && row is { } latest)
            Latest = latest;
        else
        {
            if (error.LatestCategories is { } collection)
                Apply(collection);
            ApplyFailure(error);
            if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                editorDenied = true;
            ShowError(error.Field, error.StatusCode switch
            {
                HttpStatusCode.Forbidden => "この操作を行う権限がありません。入力内容は保存されていません。",
                HttpStatusCode.NotFound => "対象は削除されているか、この家計簿を操作できません。",
                HttpStatusCode.TooManyRequests => "操作回数の上限に達しました。しばらく待って再試行してください。",
                _ => error.Code switch
                {
                    "category_referenced" => "参照が残っているため削除できません。使用を終了する場合は有効終了日を設定してください。",
                    "duplicate_category_name" => "同じ名前の科目が登録されています。",
                    "created_result_deleted" => "作成した科目は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして追加し直してください。",
                    "revision_conflict" => "ほかの操作で変更されています。入力を確認して再試行してください。",
                    _ => "科目の操作を完了できませんでした。入力内容を確認してください。"
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
        Income = latest.IncomeExpense;
        Tax = latest.TaxCategoryCode;
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
    private void Order() => BeginOrder(Info!.Categories.OrderBy(p => p.DisplayOrder), Info.OrderRevision);
    private void BeginOrder(IEnumerable<CategoryInfo> people, Guid revision)
    {
        OrderRevision = revision;
        orderDenied = false;
        OrderNotice = null;
        Restored = false;
        OrderRows.Clear();
        foreach (var row in people)
            OrderRows.Add(new(row));
        originalOrder = OrderRows.Select(row => row.Category.CategoryId).ToArray();
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
        var ids = OrderRows.Select(p => p.Category.CategoryId).ToArray();
        var revision = OrderRevision;
        OrderNotice = null;
        await Context.RunAsync(async () =>
        {
            try
            {
                Apply(await Context.Categories.SaveOrderAsync(revision, ids, Context.Token));
                FinishOrder();
            }
            catch (CategoryRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestCategories is { } latest)
                {
                    Apply(latest);
                    BeginOrder(latest.Categories.OrderBy(p => p.DisplayOrder), latest.OrderRevision);
                    OrderNoticeInformational = true;
                    OrderNotice = "ほかの操作で変更されています。未保存の並び順を破棄し、最新の内容に切り替えました。";
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
        originalOrder = [];
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
        if (denied is not null)
            permission = Context.Permissions;
        if (denied is null)
        {
            try
            {
                Apply(await Context.Categories.ListAsync(token));
            }
            catch (CategoryRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                ApplyFailure(error);
                denied = "現在の権限ではこの編集を保存できません。未保存の入力は残しています。";
            }
        }

        Restored = true;
        if (IsOrdering)
        {
            var selectedId = SelectedOrder?.Category.CategoryId;
            var latest = (Info?.Categories ?? []).ToDictionary(p => p.CategoryId);
            for (var i = 0; i < OrderRows.Count; i++)
            {
                var previous = OrderRows[i].Category;
                OrderRows[i] = new(latest.GetValueOrDefault(previous.CategoryId) ?? previous);
            }

            SelectedOrder = OrderRows.FirstOrDefault(row => row.Category.CategoryId == selectedId);
            orderDenied = denied is not null || !IsAdmin;
            OrderNoticeInformational = true;
            OrderNotice = denied ?? (orderDenied ? "現在の権限では保存できません。未保存の並び順は残しています。"
                : "未保存の並び順を復元しました。内容を確認してください。");
        }
        else if (HasEditor)
        {
            if (Editing is not null && Info?.Categories.Any(p => p.CategoryId == Editing.CategoryId) != true)
                missing.Add(Editing.CategoryId);
            editorDenied = denied is not null || (Editing is null ? !IsAdmin : !CanEdit(Editing));
            if (editorDenied)
                ShowError(null, denied ?? "現在の権限ではこの編集を保存できません。未保存の入力は残しています。");
        }

        NotifyState();
    }

    private void NotifyState()
    {
        foreach (var name in new[]
        {
            nameof(HasOrderChanges),
            nameof(IsAdmin),
            nameof(CanView),
            nameof(HasEditor),
            nameof(IsDeleting),
            nameof(HasLatest),
            nameof(CanSave),
            nameof(Title),
            nameof(PrimaryText),
            nameof(DeleteNotice),
            nameof(LatestName),
            nameof(LatestIncome),
            nameof(LatestTax),
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
