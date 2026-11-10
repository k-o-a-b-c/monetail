using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

/// <summary>接続/本人の現在コンテキスト内だけで使う家計簿別メモリ。期限・永続化・自動更新はない。</summary>
public sealed class LedgerMasterCache
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private long generation;
    public event Action<Guid>? Changed;
    private sealed class Slot<T> where T : class { public T? Value; public Task<T>? Pending; public long Version; }
    private sealed class Entry
    {
        public Slot<PeopleInfo> People = new(); public Slot<PaymentMethodsInfo> Payments = new(); public Slot<CategoriesInfo> Categories = new();
        public Guid? MyPersonId; public long IdentityVersion;
    }
    private Entry For(Guid ledger) { if (!entries.TryGetValue(ledger, out var entry)) entries[ledger] = entry = new(); return entry; }
    public void Clear() { lock (gate) { generation++; entries.Clear(); } }
    public PeopleInfo? People(Guid ledger) { lock (gate) return entries.GetValueOrDefault(ledger)?.People.Value; }
    public PaymentMethodsInfo? Payments(Guid ledger) { lock (gate) return entries.GetValueOrDefault(ledger)?.Payments.Value; }
    public CategoriesInfo? Categories(Guid ledger) { lock (gate) return entries.GetValueOrDefault(ledger)?.Categories.Value; }
    public Guid? MyPersonId(Guid ledger) { lock (gate) return entries.GetValueOrDefault(ledger)?.MyPersonId; }
    public void UpdatePeople(Guid ledger, PeopleInfo value) => Put(ledger, e => e.People, value, e => e.MyPersonId = value.MyPersonId);
    public void UpdatePayments(Guid ledger, PaymentMethodsInfo value) => Put(ledger, e => e.Payments, value, e => e.MyPersonId = value.MyPersonId);
    public void UpdateCategories(Guid ledger, CategoriesInfo value) => Put(ledger, e => e.Categories, value);
    public void UpdateIdentity(Guid ledger, Guid? person) { lock (gate) { var entry = For(ledger); entry.MyPersonId = person; entry.IdentityVersion++; } Changed?.Invoke(ledger); }
    private void Put<T>(Guid ledger, Func<Entry, Slot<T>> select, T value, Action<Entry>? identity = null) where T : class
    {
        lock (gate) { var entry = For(ledger); var slot = select(entry); slot.Version++; slot.Value = value; slot.Pending = null; if (identity is not null) { identity(entry); entry.IdentityVersion++; } }
        Changed?.Invoke(ledger);
    }
    public Task<PeopleInfo> GetPeopleAsync(Guid ledger, Func<CancellationToken, Task<PeopleInfo>> load, CancellationToken token = default) =>
        Get(ledger, e => e.People, load, (e, value) => e.MyPersonId = value.MyPersonId, token);
    public Task<PaymentMethodsInfo> GetPaymentsAsync(Guid ledger, Func<CancellationToken, Task<PaymentMethodsInfo>> load, CancellationToken token = default) =>
        Get(ledger, e => e.Payments, load, (e, value) => e.MyPersonId = value.MyPersonId, token);
    public Task<CategoriesInfo> GetCategoriesAsync(Guid ledger, Func<CancellationToken, Task<CategoriesInfo>> load, CancellationToken token = default) =>
        Get(ledger, e => e.Categories, load, null, token);
    private Task<T> Get<T>(Guid ledger, Func<Entry, Slot<T>> select, Func<CancellationToken, Task<T>> load,
        Action<Entry, T>? identity, CancellationToken token) where T : class
    {
        token.ThrowIfCancellationRequested(); Slot<T> slot; long epoch, version, identityVersion; TaskCompletionSource<T> completion;
        lock (gate)
        {
            var entry = For(ledger); slot = select(entry);
            if (slot.Value is not null) return Task.FromResult(slot.Value);
            if (slot.Pending is not null) return slot.Pending.WaitAsync(token);
            epoch = generation; version = slot.Version; identityVersion = entry.IdentityVersion; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); slot.Pending = completion.Task;
        }
        _ = Load(); return completion.Task;
        async Task Load()
        {
            try
            {
                var value = await load(token); var changed = false;
                lock (gate)
                {
                    if (generation == epoch && slot.Version == version)
                    {
                        slot.Value = value; var entry = For(ledger);
                        if (entry.IdentityVersion == identityVersion) identity?.Invoke(entry, value);
                        changed = true;
                    }
                    if (generation == epoch && slot.Value is not null) value = slot.Value;
                    if (slot.Version == version) slot.Pending = null;
                }
                if (changed) Changed?.Invoke(ledger);
                completion.TrySetResult(value);
            }
            catch (OperationCanceledException) { lock (gate) if (slot.Version == version) slot.Pending = null; completion.TrySetCanceled(); }
            catch (Exception error) { lock (gate) if (slot.Version == version) slot.Pending = null; completion.TrySetException(error); }
        }
    }
    public string PaymentName(Guid ledger, Guid payment)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(ledger, out var entry) || entry.Payments.Value?.PaymentMethods.FirstOrDefault(p => p.PaymentMethodId == payment) is not { } row)
                return "支払方法名未取得";
            if (row.OwnerPersonId == entry.MyPersonId) return row.Name;
            var person = entry.People.Value?.People.FirstOrDefault(p => p.PersonId == row.OwnerPersonId)?.Name ?? "人物名未取得";
            return $"（{person}）{row.Name}";
        }
    }
}
