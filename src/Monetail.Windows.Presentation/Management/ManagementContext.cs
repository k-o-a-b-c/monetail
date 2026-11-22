using CommunityToolkit.Mvvm.ComponentModel;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Management;
/// <summary>管理機能が共有する本人・家計簿とAPI権限の表示用コンテキスト。画面は保持しない。</summary>
public sealed partial class ManagementContext : ObservableObject, IDisposable
{
    private readonly Func<Func<Task>, Action<string>?, Task> execute;
    private readonly CancellationTokenSource lifetime;
    public ManagementContext(MonetailApiClient api, ClientSession session, LedgerMasterCache masters, LedgerInfo ledger, Guid accountId, string origin, Func<Func<Task>, Action<string>?, Task> execute, CancellationToken token = default)
    {
        Ledger = ledger;
        AccountId = accountId;
        Origin = origin;
        Masters = masters;
        this.execute = execute;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Permissions = new(ledger.Role, masters.MyPersonId(ledger.LedgerId));
        People = new(api, session, ledger.LedgerId);
        Categories = new(api, session, ledger.LedgerId);
        Payments = new(api, session, ledger.LedgerId);
        Participants = new(api, session, ledger.LedgerId);
    }

    public LedgerInfo Ledger { get; private set; }
    public Guid AccountId { get; }
    public string Origin { get; }
    public LedgerMasterCache Masters { get; }
    public PeopleWorkspace People { get; }
    public CategoriesWorkspace Categories { get; }
    public PaymentMethodsWorkspace Payments { get; }
    public ParticipantsWorkspace Participants { get; }
    public CancellationToken Token => lifetime.Token;
    public bool IsDisposed => lifetime.IsCancellationRequested;
    public bool CanOperate => !IsBusy && !IsSuspended && !IsDisposed;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsSuspended { get; set; }

    [ObservableProperty]
    public partial PeoplePermissions Permissions { get; set; }

    public Task RunAsync(Func<Task> action, Action<string>? failed = null) => IsDisposed ? Task.CompletedTask : execute(action, failed);
    public void ApplyPermissions(PeoplePermissions? permission)
    {
        if (!IsDisposed && permission is not null)
            Permissions = permission;
    }

    public void Reopened(LedgerInfo ledger)
    {
        Ledger = ledger;
        Permissions = new(ledger.Role, Permissions.MyPersonId);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
