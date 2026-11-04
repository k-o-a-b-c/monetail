namespace Monetail.Api.Features.Accounts;

/// <summary>本人表示名や認証権限から独立した、全体管理者専用の任意メモ。</summary>
public sealed class AccountAdminNote
{
    public const int MaxLength = 100;
    public string AuthUserId { get; set; } = null!;
    public string? AdminNote { get; set; }
}
