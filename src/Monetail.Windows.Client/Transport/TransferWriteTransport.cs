using System.Net;
using System.Text.Json.Serialization;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record TransferCreatedInfo(Guid TransferId);
public enum TransferWriteFailureKind { Connection, Timeout, InvalidResponse }
/// <summary>保存成否を断定しない。同じ要求の明示的再試行を待つ。</summary>
public sealed class TransferWriteUncertainException(TransferWriteFailureKind kind) : Exception("保存結果を確認できませんでした。")
{
    public TransferWriteFailureKind Kind { get; } = kind;
}

public sealed partial class MonetailApiClient
{
    public async Task<TransferCreatedInfo> CreateTransferAsync(string access, TransferWriteRequest request, CancellationToken token = default)
    {
        request.CheckTarget(request.LedgerId, TransferWriteOperation.Create);
        return new((await WriteTransferAsync(access, request, token))!.Value);
    }
    public async Task UpdateTransferAsync(string access, TransferWriteRequest request, CancellationToken token = default)
    {
        request.CheckTarget(request.LedgerId, TransferWriteOperation.Update); await WriteTransferAsync(access, request, token);
    }
    public async Task DeleteTransferAsync(string access, TransferWriteRequest request, CancellationToken token = default)
    {
        request.CheckTarget(request.LedgerId, TransferWriteOperation.Delete); await WriteTransferAsync(access, request, token);
    }
    private async Task<Guid?> WriteTransferAsync(string access, TransferWriteRequest request, CancellationToken token)
    {
        var method = request.Operation switch { TransferWriteOperation.Create => HttpMethod.Post, TransferWriteOperation.Update => HttpMethod.Put, _ => HttpMethod.Delete };
        var path = $"{TransferPath(request.LedgerId)}/transfers" + (request.TransferId is Guid id ? $"/{id:D}" : "");
        Guid? createdId = null;
        try
        {
            await SendResponseAsync(method, path, access, null, false, token, async (content, ct) =>
            {
                await using var stream = await content.ReadAsStreamAsync(ct);
                if (await stream.ReadAsync(new byte[1], ct) != 0) throw new ApiConnectionException();
                return true;
            }, request.Content(), successStatus: request.Operation == TransferWriteOperation.Create ? HttpStatusCode.Created : HttpStatusCode.NoContent,
                readFailure: (response, ct) => ReadTransferWriteFailureAsync(response, request.TransferId, ct),
                connectionFailure: error => new TransferWriteUncertainException(error is OperationCanceledException ? TransferWriteFailureKind.Timeout : TransferWriteFailureKind.Connection),
                validateSuccess: response => { if (request.Operation == TransferWriteOperation.Create) createdId = ReadCreatedTransferId(response, request.LedgerId); });
            return createdId;
        }
        catch (ApiConnectionException) { throw new TransferWriteUncertainException(TransferWriteFailureKind.InvalidResponse); }
    }
    private Guid ReadCreatedTransferId(HttpResponseMessage response, Guid ledgerId)
    {
        // 通常のヘッダー読取でUriの正規化が走る前に、受信したパス自体を検証する。
        if (!response.Headers.NonValidated.TryGetValues("Location", out var locations)) throw new ApiConnectionException();
        var values = locations.ToArray();
        if (values.Length != 1) throw new ApiConnectionException();
        var path = values[0];
        if (!path.StartsWith('/'))
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() || uri.Scheme != endpoint.BaseUri.Scheme ||
                uri.Authority != endpoint.BaseUri.Authority || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new ApiConnectionException();
            var start = path.IndexOf('/', path.IndexOf("://", StringComparison.Ordinal) + 3);
            path = start < 0 ? "" : path[start..];
        }
        var parts = path.Split('/');
        if (parts.Length != 5 || parts[0] != "" || parts[1] != "ledgers" || parts[3] != "transfers" ||
            parts[2].Length != 36 || !Guid.TryParseExact(parts[2], "D", out var ledger) || ledger != ledgerId ||
            parts[4].Length != 36 || !Guid.TryParseExact(parts[4], "D", out var transfer) || transfer == Guid.Empty) throw new ApiConnectionException();
        return transfer;
    }
    private static async Task<Exception> ReadTransferWriteFailureAsync(HttpResponseMessage response, Guid? transferId, CancellationToken token)
    {
        var bytes = await ReadBoundedAsync(response.Content, token);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = TransferJson.Read<TransferConflictPayload>(bytes);
            if (error.Code == "revision_conflict" && transferId is Guid id && error.Latest is not null)
                return new TransferRequestException(response.StatusCode, error.Code, error.Field, latestTransfer: ValidateTransfer(error.Latest, id));
            if (error.Code == "request_conflict" && error.Latest is null) return new TransferRequestException(response.StatusCode, error.Code, error.Field);
            throw new ApiConnectionException();
        }
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            return ReadTransferFailure(response.StatusCode, bytes);
        throw new ApiConnectionException();
    }
    private sealed record TransferConflictPayload(string Code, TransferInfo? Latest, string? Field = null)
    {
        // LedgerApiFault.Bodyの共通項目は受理するが、表示文や権限更新には使わない。
        [JsonIgnore] public object? Message => null;
        [JsonIgnore] public object? Permissions => null;
    }
}
