using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Monetail.Windows.Client.Transport;

/// <summary>家計簿の表示に必要な、サーバーで検証された情報を保持する。</summary>
public sealed record LedgerInfo(Guid LedgerId, string Name, string Role, string Revision);

/// <summary>通信自体は成立した家計簿操作の拒否を表す。サーバー本文は表示しない。</summary>
public sealed class LedgerRequestException(HttpStatusCode statusCode) : Exception("家計簿の操作を完了できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed partial class MonetailApiClient
{
    public async Task<LedgerInfo[]> ListLedgersAsync(string accessToken, CancellationToken token = default)
    {
        var bytes = await SendAsync(HttpMethod.Get, "ledgers", accessToken, null, false, token);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ApiConnectionException();
            var ledgers = document.RootElement.EnumerateArray().Select(ReadLedger).ToArray();
            if (ledgers.Select(row => row.LedgerId).Distinct().Count() != ledgers.Length) throw new ApiConnectionException();
            return ledgers;
        }
        catch (JsonException) { throw new ApiConnectionException(); }
    }

    public async Task<LedgerInfo?> OpenLastLedgerAsync(string accessToken, CancellationToken token = default)
    {
        using var document = Parse(await SendAsync(HttpMethod.Post, "ledgers/open-last", accessToken, null, false, token));
        if (!document.RootElement.TryGetProperty("ledger", out var ledger)) throw new ApiConnectionException();
        return ledger.ValueKind == JsonValueKind.Null ? null : ReadLedger(ledger);
    }

    public async Task<LedgerInfo> GetLedgerAsync(string accessToken, Guid ledgerId, CancellationToken token = default)
    {
        using var document = Parse(await SendAsync(HttpMethod.Get, $"ledgers/{ledgerId:N}", accessToken, null, false, token));
        var ledger = ReadLedger(document.RootElement);
        if (ledger.LedgerId != ledgerId) throw new ApiConnectionException();
        return ledger;
    }

    public async Task<LedgerInfo> CreateLedgerAsync(string accessToken, string name, Guid requestId, CancellationToken token = default)
    {
        using var document = Parse(await SendAsync(HttpMethod.Post, "ledgers", accessToken, null, false, token,
            JsonContent.Create(new { name, requestId })));
        return ReadLedger(document.RootElement);
    }

    public async Task<LedgerInfo> OpenLedgerAsync(string accessToken, Guid ledgerId, CancellationToken token = default)
    {
        using var document = Parse(await SendAsync(HttpMethod.Post, $"ledgers/{ledgerId:N}/open", accessToken, null, false, token));
        var ledger = ReadLedger(document.RootElement);
        if (ledger.LedgerId != ledgerId) throw new ApiConnectionException();
        return ledger;
    }

    private static LedgerInfo ReadLedger(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !Guid.TryParse(String(json, "ledgerId"), out var id) || id == Guid.Empty ||
            String(json, "name") is not { Length: > 0 and <= 100 } name || string.IsNullOrWhiteSpace(name) ||
            String(json, "role") is not ("Viewer" or "Editor" or "Admin") ||
            !Guid.TryParse(String(json, "revision"), out _)) throw new ApiConnectionException();
        return new LedgerInfo(id, name, String(json, "role")!, String(json, "revision")!);
    }
}
