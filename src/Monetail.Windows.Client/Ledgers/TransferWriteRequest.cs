using System.Text;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed record TransferSnapshot(DateTime TransactionAt, string Name, Guid FromPaymentMethodId, Guid ToPaymentMethodId, int Amount, string? Memo);
public enum TransferWriteOperation { Create, Update, Delete }

/// <summary>一操作の家計簿・対象・版・JSONを固定する。結果不明時は同じ要求を再送する。</summary>
public sealed class TransferWriteRequest
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public Guid LedgerId { get; }
    public Guid? TransferId { get; }
    public Guid? Revision { get; }
    public TransferWriteOperation Operation { get; }
    private readonly string json;

    private TransferWriteRequest(Guid ledgerId, Guid? transferId, Guid? revision, TransferWriteOperation operation, TransferSnapshot? snapshot)
    {
        TransferQueries.Id(ledgerId, "ledgerId");
        if (transferId is Guid id) TransferQueries.Id(id, "transferId");
        if (revision is Guid version && (version.Version != 4 || (version.ToByteArray()[8] & 0xc0) != 0x80))
            throw new TransferInputException("revision", "取得したUUID v4の更新版を指定してください。");
        if (snapshot is not null) ValidateSnapshot(snapshot);
        LedgerId = ledgerId; TransferId = transferId; Revision = revision; Operation = operation;
        var body = new Dictionary<string, object?> { ["requestId"] = RequestId };
        if (revision is not null) body["revision"] = revision;
        if (snapshot is not null)
        {
            body["transactionAt"] = snapshot.TransactionAt; body["name"] = snapshot.Name;
            body["fromPaymentMethodId"] = snapshot.FromPaymentMethodId; body["toPaymentMethodId"] = snapshot.ToPaymentMethodId;
            body["amount"] = snapshot.Amount; body["memo"] = snapshot.Memo is "" ? null : snapshot.Memo;
        }
        json = TransferJson.Serialize(body);
    }
    public static TransferWriteRequest Create(Guid ledgerId, TransferSnapshot snapshot) => new(ledgerId, null, null, TransferWriteOperation.Create, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    public static TransferWriteRequest Update(Guid ledgerId, Guid transferId, Guid revision, TransferSnapshot snapshot) => new(ledgerId, transferId, revision, TransferWriteOperation.Update, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    public static TransferWriteRequest Delete(Guid ledgerId, Guid transferId, Guid revision) => new(ledgerId, transferId, revision, TransferWriteOperation.Delete, null);
    internal HttpContent Content() => new StringContent(json, Encoding.UTF8, "application/json");
    internal void CheckTarget(Guid ledgerId, TransferWriteOperation operation)
    {
        if (LedgerId != ledgerId || Operation != operation) throw new TransferInputException("requestId", "元の家計簿・操作の要求を使用してください。");
    }
    private static void ValidateSnapshot(TransferSnapshot snapshot)
    {
        static void Check(bool valid, string field) { if (!valid) throw new TransferInputException(field, "入力内容を確認してください。"); }
        Check(snapshot.TransactionAt.Ticks % TimeSpan.TicksPerSecond == 0, "transactionAt");
        Check(!string.IsNullOrWhiteSpace(snapshot.Name) && snapshot.Name.Length <= 100, "name");
        TransferQueries.Id(snapshot.FromPaymentMethodId, "fromPaymentMethodId"); TransferQueries.Id(snapshot.ToPaymentMethodId, "toPaymentMethodId");
        Check(snapshot.FromPaymentMethodId != snapshot.ToPaymentMethodId, "toPaymentMethodId");
        Check(snapshot.Amount > 0, "amount");
        Check(snapshot.Memo is null || snapshot.Memo.Length <= 100 && snapshot.Memo.IndexOfAny(['\r', '\n', '\v', '\f', '\u0085', '\u2028', '\u2029']) < 0, "memo");
    }
}
