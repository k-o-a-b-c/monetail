using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monetail.Api.Features.Monetail.Operations;

/// <summary>成功した作成操作の再送照合と結果IDを共通形式で保持する。</summary>
public sealed class OperationRequest
{
    public string RequestId { get; set; } = null!;
    public string OperationType { get; set; } = null!;
    public string ActorAuthUserId { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string ResultId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>正規化済みの対象範囲と入力を、区切りの曖昧さなしで照合する。</summary>
    public static string Hash(params string?[] values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));

    /// <summary>要求IDだけを権限の根拠にせず、操作・操作者・内容も確認する。</summary>
    public bool Matches(string operation, string actor, string hash) =>
        OperationType == operation && ActorAuthUserId == actor && RequestHash == hash;
}
