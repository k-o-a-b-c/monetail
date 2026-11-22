using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
/// <summary>支払方法の表示・編集・順序の唯一の画面状態。保存値と要求IDは既存Clientの型をそのまま使う。</summary>
public sealed partial class PaymentMethodsManagementViewModel : ObservableObject, IDisposable
{
    public ManagementContext Context { get; }
    public PaymentMethodsInfo? Info { get; private set; }
    public ObservableCollection<PaymentRow> OrderRows { get; } = [];

    private readonly HashSet<Guid> missing = [];
    public PaymentMethodInfo? Editing { get; private set; }
    public PaymentMethodInfo? Latest { get; private set; }
    public PaymentMethodCreationDraft? Creation { get; private set; }
    public Guid OrderRevision { get; private set; }

    private bool editorDenied, orderDenied;
    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    public partial PaymentRow? Selected { get; set; }

    [ObservableProperty]
    public partial PaymentRow? SelectedOrder { get; set; }

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

    public PaymentMethodsManagementViewModel(ManagementContext context)
    {
        Context = context;
        context.PropertyChanged += ContextChanged;
    }

    public event Func<Task>? EditorRequested;
    public bool IsAdmin => permission?.CurrentRole == "Admin";
    public PeopleInfo? People { get; private set; }
    public ObservableCollection<PaymentGroup> Groups { get; } = [];
    public PersonRow[] Owners { get; private set; } = [];

    [ObservableProperty]
    public partial PersonRow? SelectedOwner { get; set; }
    public Guid? ChosenOwner => Editing?.OwnerPersonId ?? (permission?.CurrentRole == "Editor" ? permission.MyPersonId : SelectedOwner?.Person.PersonId);
    public bool ChooseOwner => Editing is null && IsAdmin;
    public string OwnerName => OwnerLabel(Editing?.OwnerPersonId ?? permission?.MyPersonId ?? Guid.Empty);
    public string LatestOwner => OwnerLabel(Latest?.OwnerPersonId ?? Guid.Empty);
    public string OrderOwnerName => OwnerLabel(OrderOwner);
    public Guid OrderOwner { get; private set; }

    public string OwnerLabel(Guid owner) => People?.People.FirstOrDefault(p => p.PersonId == owner)?.Name ?? "人物情報を確認できません";
    public bool CanManageOwner(Guid owner) => Info?.OwnerOrderRevisions.Any(p => p.PersonId == owner) == true && (IsAdmin || permission?.CurrentRole == "Editor" && permission.MyPersonId == owner);
    partial void OnSelectedOwnerChanged(PersonRow? value) => NotifyState();
    public bool CanView => permission is null || permission.CurrentRole is not null;

    private PeoplePermissions? permission;
    public bool HasEditor => Operation != ManagementEditorOperation.None;
    public bool IsDeleting => Operation == ManagementEditorOperation.Delete;
    public bool HasLatest => Latest is not null;
    public bool CanRestoreAuthentication => IsOrdering || HasEditor && !IsDeleting;
    public bool IsEmpty => Groups.Count == 0;

    public string Title => IsDeleting ? "支払方法の削除" : Editing is null ? "支払方法の追加" : "支払方法の編集";
    public string PrimaryText => IsDeleting ? "削除" : Editing is null ? "登録" : "保存";
    public string DeleteNotice => $"「{OwnerLabel(Editing?.OwnerPersonId ?? Guid.Empty)}」の「{Editing?.Name}」を削除しますか。参照がある支払方法は削除できません。";

    public string LatestName => Latest?.Name ?? "";
    public string LatestFrom => DateTimeInput.FormatDate(Latest?.ValidFrom);
    public string LatestTo => DateTimeInput.FormatDate(Latest?.ValidTo);
    public bool CanSave => Context.CanOperate && HasEditor && !editorDenied && Latest is null && (IsDeleting ? Editing is not null && CanDelete(Editing) : Editing is null ? ChosenOwner is { } owner && CanManageOwner(owner) : CanEdit(Editing));

    private bool CanEdit(PaymentMethodInfo row) => CanManageOwner(row.OwnerPersonId) && !missing.Contains(row.PaymentMethodId);
    private bool CanDelete(PaymentMethodInfo row) => CanEdit(row);
    private bool CanAdd() => Context.CanOperate && Info?.OwnerOrderRevisions.Any(p => CanManageOwner(p.PersonId)) == true && !HasEditor && !IsOrdering;
    private bool CanEditSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanEdit(row.Payment);
    private bool CanDeleteSelected() => Context.CanOperate && !HasEditor && !IsOrdering && Selected is { } row && CanDelete(row.Payment);
    private bool CanOrder(Guid owner) => Context.CanOperate && !HasEditor && !IsOrdering && CanManageOwner(owner);
    private bool CanSaveOrder() => Context.CanOperate && IsOrdering && !orderDenied && CanManageOwner(OrderOwner);
    private bool CanMoveUp() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) > 0;
    private bool CanMoveDown() => CanSaveOrder() && SelectedOrder is not null && OrderRows.IndexOf(SelectedOrder) < OrderRows.Count - 1;
    partial void OnIncludeInactiveChanged(bool value) => RenderRows();
    partial void OnSelectedChanged(PaymentRow? value) => NotifyState();
    partial void OnSelectedOrderChanged(PaymentRow? value) => NotifyState();
    private void ContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        NotifyState();
    }

    public Task LoadAsync(CancellationToken token) => Context.ReadAsync(async () =>
    {
        try
        {
            var people = await Context.People.ListAsync(token);
            var result = await Context.Payments.ListAsync(token);
            if (!token.IsCancellationRequested && !Context.IsDisposed)
            {
                People = people;
                Context.Masters.UpdatePeople(Context.Ledger.LedgerId, people);
                Apply(result);
            }
        }
        catch (PeopleRequestException error)
        {
            token.ThrowIfCancellationRequested();
            var failure = new PaymentMethodRequestException(error.StatusCode, error.Code, error.Field, permissions: error.Permissions);
            ApplyFailure(failure);
            throw failure;
        }
        catch (PaymentMethodRequestException error)
        {
            token.ThrowIfCancellationRequested();
            ApplyFailure(error);
            throw;
        }
    }, token, message =>
    {
        if (!token.IsCancellationRequested)
            ListError = message;
    });
    public void Apply(PaymentMethodsInfo info)
    {
        if (Context.IsDisposed)
            return;
        Info = info;
        missing.Clear();
        permission = new(info.CurrentRole, info.MyPersonId);
        Context.ApplyPermissions(permission);
        Context.Masters.UpdatePayments(Context.Ledger.LedgerId, info);
        ListError = null;
        RenderRows();
    }

    private void RenderRows()
    {
        // 再取得・期間外フィルターでは従来どおり選択を解除する。
        Selected = null;
        Groups.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var owner in (Info?.OwnerOrderRevisions ?? []).OrderByDescending(p => p.PersonId == permission?.MyPersonId))
        {
            var rows = (Info?.PaymentMethods ?? []).Where(p => p.OwnerPersonId == owner.PersonId && (IncludeInactive || (p.ValidFrom is null || p.ValidFrom <= today) && (p.ValidTo is null || p.ValidTo >= today))).OrderBy(p => p.DisplayOrder).ThenBy(p => p.PaymentMethodId.ToString("N"), StringComparer.Ordinal).Select(p => new PaymentRow(p)).ToArray();
            if (rows.Length != 0)
                Groups.Add(new(owner.PersonId, OwnerLabel(owner.PersonId), CanManageOwner(owner.PersonId), rows));
        }

        OnPropertyChanged(nameof(IsEmpty));
        var selectedOwnerId = SelectedOwner?.Person.PersonId;
        Owners = (People?.People ?? []).Where(p => Info?.OwnerOrderRevisions.Any(o => o.PersonId == p.PersonId) == true)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.PersonId)
            .Select(p => new PersonRow(p, p.PersonId == permission?.MyPersonId)).ToArray();
        OnPropertyChanged(nameof(Owners));
        SelectedOwner = Owners.FirstOrDefault(row => row.Person.PersonId == selectedOwnerId);
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync() => OpenAsync(null, false);
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private Task EditAsync() => OpenAsync(Selected!.Payment, false);
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteAsync() => OpenAsync(Selected!.Payment, true);
    private async Task OpenAsync(PaymentMethodInfo? person, bool deletion)
    {
        Editing = person;
        Latest = null;
        Creation = null;
        editorDenied = false;
        SelectedOwner = null;
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
        PaymentMethodInput? input = null;
        if (!IsDeleting)
        {
            try
            {
                input = new(Name, from, to);
            }
            catch (PaymentMethodInputException error)
            {
                ShowError(error.Field, error.Message);
                return false;
            }

            if (Editing is null && (Creation?.Input != input || Creation.OwnerPersonId != ChosenOwner))
                Creation = new(ChosenOwner!.Value, input);
        }

        var saved = false;
        await Context.RunAsync(async () =>
        {
            try
            {
                var result = IsDeleting ? await Context.Payments.DeleteAsync(Editing!, Context.Token) : Editing is null ? (await Context.Payments.CreateAsync(Creation!, Context.Token)).State : await Context.Payments.UpdateAsync(Editing, new(input!.Name, new(input.ValidFrom), new(input.ValidTo)), Context.Token);
                Apply(result);
                saved = !Context.IsDisposed;
            }
            catch (PaymentMethodRequestException error)
            {
                HandleFailure(error);
            }
        }, message => ShowError(null, message));
        return saved;
    }

    public void ApplyFailure(PaymentMethodRequestException error)
    {
        if (error.Permissions is { } current)
            permission = current;
        else if (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            permission = new(null, null);
        if (error.StatusCode == HttpStatusCode.NotFound && Editing is not null)
            missing.Add(Editing.PaymentMethodId);
        Context.ApplyPermissions(permission);
        RenderRows();
    }

    private void HandleFailure(PaymentMethodRequestException error)
    {
        var row = error.LatestPaymentMethod;
        if (error.Code == "revision_conflict" && row is { } latest)
            Latest = latest;
        else
        {
            if (error.LatestPaymentMethods is { } collection)
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
                    "payment_method_referenced" => "参照が残っているため削除できません。使用を終了する場合は有効終了日を設定してください。",
                    "duplicate_payment_method_name" => "この人物には同じ名前の支払方法が登録されています。",
                    "created_result_deleted" => "作成した支払方法は削除されています。",
                    "request_conflict" => "この登録操作を続けられません。キャンセルして追加し直してください。",
                    "revision_conflict" => "ほかの操作で変更されています。入力を確認して再試行してください。",
                    _ => "支払方法の操作を完了できませんでした。入力内容を確認してください。"
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
        SelectedOwner = null;
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
    private void Order(Guid owner) => BeginOrder(owner, Info!.OwnerOrderRevisions.Single(p => p.PersonId == owner).OrderRevision, Info.PaymentMethods.Where(p => p.OwnerPersonId == owner).OrderBy(p => p.DisplayOrder).ThenBy(p => p.PaymentMethodId.ToString("N"), StringComparer.Ordinal));
    private void BeginOrder(Guid owner, Guid revision, IEnumerable<PaymentMethodInfo> people)
    {
        OrderOwner = owner;
        OrderRevision = revision;
        orderDenied = false;
        OrderNotice = null;
        Restored = false;
        OrderRows.Clear();
        foreach (var row in people)
            OrderRows.Add(new(row));
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
        var ids = OrderRows.Select(p => p.Payment.PaymentMethodId).ToArray();
        var revision = OrderRevision;
        OrderNotice = null;
        await Context.RunAsync(async () =>
        {
            try
            {
                Apply(await Context.Payments.SaveOrderAsync(OrderOwner, revision, ids, Context.Token));
                FinishOrder();
            }
            catch (PaymentMethodRequestException error)
            {
                if (error.Code == "revision_conflict" && error.LatestOrder is { } latest && Info is not null)
                {
                    Info = Info with
                    {
                        PaymentMethods = Info.PaymentMethods.Where(p => p.OwnerPersonId != OrderOwner).Concat(latest.PaymentMethods).ToArray(),
                        OwnerOrderRevisions = Info.OwnerOrderRevisions.Select(p => p.PersonId == OrderOwner ? p with { OrderRevision = latest.OrderRevision } : p).ToArray()
                    };
                    RenderRows();
                    BeginOrder(OrderOwner, latest.OrderRevision, latest.PaymentMethods);
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
                People = await Context.People.ListAsync(token);
                var payments = await Context.Payments.ListAsync(token);
                Context.Masters.UpdatePeople(Context.Ledger.LedgerId, People);
                Apply(payments);
            }
            catch (PeopleRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                permission = error.Permissions ?? new(null, null);
                Context.ApplyPermissions(permission);
                denied = "現在の権限では操作できません。未保存の入力は残しています。";
            }
            catch (PaymentMethodRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                ApplyFailure(error);
                denied = "現在の権限ではこの編集を保存できません。未保存の入力は残しています。";
            }
        }

        Restored = true;
        if (IsOrdering)
        {
            var selectedId = SelectedOrder?.Payment.PaymentMethodId;
            var latest = (Info?.PaymentMethods ?? []).ToDictionary(p => p.PaymentMethodId);
            for (var i = 0; i < OrderRows.Count; i++)
            {
                var previous = OrderRows[i].Payment;
                OrderRows[i] = new(latest.GetValueOrDefault(previous.PaymentMethodId) ?? previous);
            }

            SelectedOrder = OrderRows.FirstOrDefault(row => row.Payment.PaymentMethodId == selectedId);
            orderDenied = denied is not null || !CanManageOwner(OrderOwner);
            OrderNoticeInformational = true;
            OrderNotice = denied ?? (orderDenied ? "現在の権限では保存できません。未保存の並び順は残しています。"
                : "未保存の並び順を復元しました。内容を確認してください。");
        }
        else if (HasEditor)
        {
            if (Editing is not null && Info?.PaymentMethods.Any(p => p.PaymentMethodId == Editing.PaymentMethodId) != true)
                missing.Add(Editing.PaymentMethodId);
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
            nameof(ChooseOwner),
            nameof(OwnerName),
            nameof(LatestOwner),
            nameof(OrderOwnerName),
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
