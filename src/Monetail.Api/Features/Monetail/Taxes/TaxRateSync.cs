using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Monetail.Api.Persistence;

namespace Monetail.Api.Features.Monetail.Taxes;

/// <summary>全履歴を希望状態として検証する。DBの保存文字列とは別にDecimalで比較する。</summary>
internal sealed record TaxRateSyncRow(string TaxCategoryCode, DateOnly ValidFrom, DateOnly? ValidTo, decimal? Rate)
{
    public static TaxRateSyncRow[] Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        Properties(document.RootElement, ["rates"]);
        var rates = document.RootElement.GetProperty("rates");
        if (rates.ValueKind != JsonValueKind.Array) throw Invalid();
        var rows = new List<TaxRateSyncRow>();
        foreach (var row in rates.EnumerateArray())
        {
            Properties(row, ["taxCategoryCode", "validFrom", "validTo", "rate"]);
            var code = Text(row.GetProperty("taxCategoryCode"));
            var from = Date(Text(row.GetProperty("validFrom")));
            var to = row.GetProperty("validTo"); var rate = row.GetProperty("rate");
            rows.Add(new(code, from, to.ValueKind == JsonValueKind.Null ? null : Date(Text(to)),
                rate.ValueKind == JsonValueKind.Null ? null : Decimal(Text(rate))));
        }
        return Validate(rows);
    }
    public static TaxRateSyncRow[] Validate(IEnumerable<TaxRateSyncRow> source)
    {
        var rows = source.OrderBy(r => r.TaxCategoryCode, StringComparer.Ordinal).ThenBy(r => r.ValidFrom).ToArray();
        TaxRateSyncRow? previous = null;
        foreach (var row in rows)
        {
            if (row.TaxCategoryCode is not ("Standard" or "ReducedFood" or "ReducedNewspaper" or "Exempt") ||
                row.ValidTo < row.ValidFrom || row.TaxCategoryCode == "Exempt" && row.Rate is not null) throw Invalid();
            if (previous?.TaxCategoryCode == row.TaxCategoryCode && (previous.ValidTo is null || row.ValidFrom <= previous.ValidTo))
                throw new InvalidOperationException("同一税区分の期間が重複しています（境界日を含む）。");
            previous = row;
        }
        return rows;
    }
    private static void Properties(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var actual = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != names.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(names)) throw Invalid();
    }
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();
    internal static DateOnly Date(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw Invalid();
    internal static decimal Decimal(string value)
    {
        // TryParseだけでは過剰な小数桁を丸めるため、元の十進表現との一致も検証する。
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) ||
            Canonical(value) != Canonical(rate.ToString("0.############################", CultureInfo.InvariantCulture))) throw Invalid();
        return rate;
    }
    private static string Canonical(string value)
    {
        var negative = value.StartsWith('-'); var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }
    private static InvalidOperationException Invalid() => new("税率履歴JSONまたはDBの値が契約に適合しません。区分・必須4項目・日付・Decimal文字列・期間を確認してください。");
}

/// <summary>重複制約を有効に保ち、全履歴を明示的に同期する。</summary>
internal static class TaxRateSync
{
    private sealed record Stored(string Id, TaxRateSyncRow Row);
    private sealed record Change(Stored? Before, TaxRateSyncRow? After);
    public static async Task RunAsync(ApiDbContext database, TaxRateSyncRow[] desired, bool apply, bool allowDelete,
        TextWriter output, CancellationToken token)
    {
        // dry-runはReadOnly、applyもReadWriteで既存ファイルだけを開く。作成モードは使わない。
        var options = new SqliteConnectionStringBuilder(database.Database.GetConnectionString())
        { Mode = apply ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly, ForeignKeys = true };
        await using var connection = new SqliteConnection(options.ToString());
        database.Database.SetDbConnection(connection);
        await connection.OpenAsync(token);
        // BEGIN IMMEDIATEで最新状態の読取より先に書込権を確保する。dry-runのみ遅延読取。
        await using var transaction = connection.BeginTransaction(deferred: !apply);
        await using var enlistment = await database.Database.UseTransactionAsync(transaction, token);
        await DatabaseInitializer.EnsureReadyAsync(database, token);
        var current = new List<Stored>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id,tax_category_code,valid_from,valid_to,rate FROM tax_rate_history";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                current.Add(new(reader.GetString(0), new(reader.GetString(1), TaxRateSyncRow.Date(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : TaxRateSyncRow.Date(reader.GetString(3)), reader.IsDBNull(4) ? null : TaxRateSyncRow.Decimal(reader.GetString(4)))));
        }
        _ = TaxRateSyncRow.Validate(current.Select(r => r.Row));
        desired = TaxRateSyncRow.Validate(desired);
        var existing = current.ToDictionary(r => (r.Row.TaxCategoryCode, r.Row.ValidFrom));
        var wanted = desired.ToDictionary(r => (r.TaxCategoryCode, r.ValidFrom));
        var changes = current.Where(r => !wanted.ContainsKey((r.Row.TaxCategoryCode, r.Row.ValidFrom))).Select(r => new Change(r, null))
            .Concat(desired.Select(r => new Change(existing.GetValueOrDefault((r.TaxCategoryCode, r.ValidFrom)), r)))
            .Where(c => c.Before?.Row != c.After).OrderBy(c => (c.After ?? c.Before!.Row).TaxCategoryCode, StringComparer.Ordinal)
            .ThenBy(c => (c.After ?? c.Before!.Row).ValidFrom).ToArray();
        await output.WriteLineAsync("注意: 履歴変更により過去レシートが現行マスタの保存検証に不適合となる場合があります。保存済みレシートは変更しません。");
        await output.WriteLineAsync($"追加: {changes.Count(c => c.Before is null)}, 変更: {changes.Count(c => c.Before is not null && c.After is not null)}, 削除: {changes.Count(c => c.After is null)}");
        foreach (var change in changes)
        {
            var row = change.After ?? change.Before!.Row;
            await output.WriteLineAsync($"{row.TaxCategoryCode} {row.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {Describe(change.Before?.Row)} -> {Describe(change.After)}");
        }
        if (!apply) return;
        if (!allowDelete && changes.Any(c => c.After is null))
            throw new InvalidOperationException("削除候補があります。DBは変更しません。削除する場合は--apply --allow-deleteを明示してください。");
        // 開始日は不変。不要な区間を除き、保持区間を更新してから新規区間を挿入する。
        // 保持行どうしで開始日をまたぐ拡張は希望状態の重複検証で拒否される。
        foreach (var change in changes.OrderBy(c => c.After is null ? 0 : c.Before is not null ? 1 : 2))
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            if (change.After is null) command.CommandText = "DELETE FROM tax_rate_history WHERE id=$id";
            else
            {
                command.CommandText = change.Before is null
                    ? "INSERT INTO tax_rate_history(id,revision,tax_category_code,valid_from,valid_to,rate) VALUES($id,$revision,$code,$from,$to,$rate)"
                    : "UPDATE tax_rate_history SET revision=$revision,valid_to=$to,rate=$rate WHERE id=$id";
                command.Parameters.AddWithValue("$revision", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("$code", change.After.TaxCategoryCode);
                command.Parameters.AddWithValue("$from", change.After.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$to", (object?)change.After.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
                command.Parameters.AddWithValue("$rate", (object?)change.After.Rate?.ToString(CultureInfo.InvariantCulture) ?? DBNull.Value);
            }
            command.Parameters.AddWithValue("$id", change.Before?.Id ?? Guid.NewGuid().ToString("N"));
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        await output.WriteLineAsync("税率履歴を同期しました。");
    }
    private static string Describe(TaxRateSyncRow? row) => row is null ? "(なし)" :
        $"終了日={row.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null"}, 税率={row.Rate?.ToString(CultureInfo.InvariantCulture) ?? "null"}";
}
