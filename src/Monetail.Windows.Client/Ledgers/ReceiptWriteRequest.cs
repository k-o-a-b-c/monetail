using System.Text;
using System.Text.Json.Serialization;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Ledgers;

public sealed record ReceiptSnapshot(DateTime TransactionAt, string ShopName, string? ShopLocation, Guid PaymentMethodId,
    int TaxInclusionMode, string TaxRoundingMode, int? RecurrenceInterval, string? RecurrenceUnit, string? Memo,
    IReadOnlyList<ReceiptGroupSnapshot> Groups);
public sealed record ReceiptGroupSnapshot(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? GroupId,
    Guid CategoryId, string? Memo, IReadOnlyList<ReceiptDetailSnapshot> Details);
public sealed record ReceiptDetailSnapshot(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DetailId,
    int DisplayOrder, string Name, int UnitPrice, int Quantity, int UnitDiscountAmount, decimal DiscountRate, decimal? TaxRate,
    string ExpenseType, string OccurrenceType, string? Memo, IReadOnlyList<Guid> PersonIds);
public enum ReceiptWriteOperation { Create, Update, Delete }

/// <summary>一操作の対象・版・JSONを固定する。入力の配列を後で変更しても再送本文は変わらない。</summary>
public sealed class ReceiptWriteRequest
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public Guid LedgerId { get; }
    public Guid? ReceiptId { get; }
    public Guid? Revision { get; }
    public ReceiptWriteOperation Operation { get; }
    private readonly string json;
    private ReceiptWriteRequest(Guid ledgerId, Guid? receiptId, Guid? revision, ReceiptWriteOperation operation, ReceiptSnapshot? snapshot)
    {
        ReceiptQueries.Id(ledgerId, "ledgerId");
        if (receiptId is Guid id) ReceiptQueries.Id(id, "receiptId");
        if (revision is Guid version) ReceiptQueries.Id(version, "revision");
        if (snapshot is not null) ValidateSnapshot(snapshot);
        LedgerId = ledgerId; ReceiptId = receiptId; Revision = revision; Operation = operation;
        var body = new Dictionary<string, object?> { ["requestId"] = RequestId };
        if (revision is not null) body["revision"] = revision;
        if (snapshot is not null)
        {
            body["transactionAt"] = snapshot.TransactionAt; body["shopName"] = snapshot.ShopName; body["shopLocation"] = snapshot.ShopLocation;
            body["paymentMethodId"] = snapshot.PaymentMethodId; body["taxInclusionMode"] = snapshot.TaxInclusionMode; body["taxRoundingMode"] = snapshot.TaxRoundingMode;
            body["recurrenceInterval"] = snapshot.RecurrenceInterval; body["recurrenceUnit"] = snapshot.RecurrenceUnit; body["memo"] = snapshot.Memo; body["groups"] = snapshot.Groups;
        }
        json = ReceiptJson.Serialize(body);
    }
    public static ReceiptWriteRequest Create(Guid ledgerId, ReceiptSnapshot snapshot) => new(ledgerId, null, null, ReceiptWriteOperation.Create, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    public static ReceiptWriteRequest Update(Guid ledgerId, Guid receiptId, Guid revision, ReceiptSnapshot snapshot) => new(ledgerId, receiptId, revision, ReceiptWriteOperation.Update, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    public static ReceiptWriteRequest Delete(Guid ledgerId, Guid receiptId, Guid revision) => new(ledgerId, receiptId, revision, ReceiptWriteOperation.Delete, null);
    internal HttpContent Content() => new StringContent(json, Encoding.UTF8, "application/json");
    internal void CheckTarget(Guid ledger, ReceiptWriteOperation operation)
    {
        if (LedgerId != ledger || Operation != operation) throw new ReceiptInputException("requestId", "元の家計簿・操作の要求を使用してください。");
    }
    internal static void ValidateSnapshot(ReceiptSnapshot snapshot)
    {
        static void Check(bool valid, string field) { if (!valid) throw new ReceiptInputException(field, "入力内容を確認してください。"); }
        Check(snapshot.TransactionAt.Ticks % TimeSpan.TicksPerSecond == 0, "transactionAt");
        Check(snapshot.ShopName is { Length: > 0 and <= 100 }, "shopName"); ReceiptQueries.Text(snapshot.ShopLocation, "shopLocation");
        ReceiptQueries.Id(snapshot.PaymentMethodId, "paymentMethodId");
        Check(snapshot.TaxInclusionMode is 0 or 1, "taxInclusionMode"); Check(snapshot.TaxRoundingMode is "floor" or "half_up", "taxRoundingMode");
        Check(snapshot.RecurrenceInterval is null or > 0, "recurrenceInterval");
        Check((snapshot.RecurrenceInterval is null) == (snapshot.RecurrenceUnit is null) && snapshot.RecurrenceUnit is null or "Week" or "Month" or "Year", "recurrenceUnit");
        Check(snapshot.Groups is { Count: > 0 }, "groups");
        var categories = new HashSet<Guid>(); var groups = new HashSet<Guid>(); var details = new HashSet<Guid>(); var regular = false;
        for (var i = 0; i < snapshot.Groups!.Count; i++)
        {
            var group = snapshot.Groups[i]; var path = $"groups[{i}]"; Check(group is not null, path);
            ReceiptQueries.Id(group!.CategoryId, path + ".categoryId"); Check(categories.Add(group.CategoryId), path + ".categoryId");
            if (group.GroupId is Guid groupId) { ReceiptQueries.Id(groupId, path + ".groupId"); Check(groups.Add(groupId), path + ".groupId"); }
            Check(group.Details is { Count: > 0 }, path + ".details");
            for (var j = 0; j < group.Details!.Count; j++)
            {
                var row = group.Details[j]; var field = $"{path}.details[{j}]"; Check(row is not null, field);
                if (row!.DetailId is Guid detailId) { ReceiptQueries.Id(detailId, field + ".detailId"); Check(details.Add(detailId), field + ".detailId"); }
                ValidateDetail(row, field); regular |= row.OccurrenceType == "Regular";
            }
        }
        Check(snapshot.RecurrenceInterval is null || regular, "groups");
    }
    internal static void ValidateDetail(ReceiptDetailSnapshot row, string field)
    {
        static void Check(bool valid, string field) { if (!valid) throw new ReceiptInputException(field, "入力内容を確認してください。"); }
        Check(row.DisplayOrder >= 0, field + ".displayOrder"); Check(row.Name is { Length: <= 100 }, field + ".name");
        Check(row.Quantity > 0, field + ".quantity"); Check(row.DiscountRate is >= 0 and <= 100, field + ".discountRate");
        Check(row.ExpenseType is "Fixed" or "Variable" or "Excluded", field + ".expenseType");
        Check(row.OccurrenceType is "Regular" or "Irregular" or "Temporary", field + ".occurrenceType");
        Check(row.PersonIds is not null, field + ".personIds"); var people = new HashSet<Guid>();
        for (var k = 0; k < row.PersonIds!.Count; k++) { var person = row.PersonIds[k]; ReceiptQueries.Id(person, field + $".personIds[{k}]"); Check(people.Add(person), field + $".personIds[{k}]"); }
    }

}
