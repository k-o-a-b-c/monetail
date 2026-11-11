using System.Net;
using System.Text.Json.Serialization;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public enum ReceiptWriteFailureKind { Connection, Timeout, InvalidResponse }
/// <summary>保存済み/未保存のどちらとも断定できない。同じ要求を保持して利用者の再試行を待つ。</summary>
public sealed class ReceiptWriteUncertainException(ReceiptWriteFailureKind kind) : Exception("保存結果を確認できませんでした。")
{
    public ReceiptWriteFailureKind Kind { get; } = kind;
}

public sealed partial class MonetailApiClient
{
    public async Task WriteReceiptAsync(string access, ReceiptWriteRequest request, CancellationToken token = default)
    {
        var method = request.Operation switch { ReceiptWriteOperation.Create => HttpMethod.Post, ReceiptWriteOperation.Update => HttpMethod.Put, _ => HttpMethod.Delete };
        var path = $"{ReceiptPath(request.LedgerId)}/receipts" + (request.ReceiptId is Guid id ? $"/{id:D}" : "");
        try
        {
            await SendResponseAsync(method, path, access, null, false, token, async (content, ct) =>
            {
                await using var stream = await content.ReadAsStreamAsync(ct);
                if (await stream.ReadAsync(new byte[1], ct) != 0) throw new ApiConnectionException();
                return true;
            }, request.Content(), successStatus: request.Operation == ReceiptWriteOperation.Create ? HttpStatusCode.Created : HttpStatusCode.NoContent,
                readFailure: (response, ct) => ReadReceiptWriteFailureAsync(response, request.ReceiptId, ct),
                connectionFailure: error => new ReceiptWriteUncertainException(error is OperationCanceledException ? ReceiptWriteFailureKind.Timeout : ReceiptWriteFailureKind.Connection));
        }
        catch (ApiConnectionException) { throw new ReceiptWriteUncertainException(ReceiptWriteFailureKind.InvalidResponse); }
    }
    private static async Task<Exception> ReadReceiptWriteFailureAsync(HttpResponseMessage response, Guid? receiptId, CancellationToken token)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await ReceiptJson.ReadAsync<ReceiptConflictPayload>(response.Content, token);
            if (error.Code == "revision_conflict" && receiptId is Guid id && error.Latest is not null)
                return new ReceiptRequestException(response.StatusCode, error.Code, error.Field, latestReceipt: ValidateReceipt(error.Latest, id));
            if (error.Code == "request_conflict" && error.Latest is null) return new ReceiptRequestException(response.StatusCode, error.Code, error.Field);
            throw new ApiConnectionException();
        }
        var bytes = await ReadBoundedAsync(response.Content, token);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            return ReadReceiptFailure(response.StatusCode, bytes);
        throw new ApiConnectionException();
    }
    private sealed record ReceiptConflictPayload(string Code, ReceiptInfo? Latest = null, string? Field = null)
    {
        // LedgerApiFault.Bodyの共通項目を受理するが、409の表示文や権限更新には使わない。
        [JsonIgnore] public object? Message => null;
        [JsonIgnore] public object? Permissions => null;
    }
}
