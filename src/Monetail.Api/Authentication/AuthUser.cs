using Microsoft.AspNetCore.Identity;

namespace Monetail.Api.Authentication;

/// <summary>人物から独立したログイン用アカウントを管理する。</summary>
public sealed class AuthUser : IdentityUser
{
    /// <summary>初回登録で受け付ける表示名の最大文字数を定める。</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>人物名と同期しないアカウント自身の表示名を保持する。</summary>
    public string? DisplayName { get; set; }

    /// <summary>パスキー登録を完了したアカウントの利用可否を保持する。</summary>
    public bool IsEnabled { get; set; }
}

/// <summary>旧版の人物対応を履歴として保持する。新規発行・認証には使用しない。</summary>
public sealed class AuthUserLink
{
    public string AuthUserId { get; set; } = null!;
    public int UserCode { get; set; }
}

/// <summary>既存人物の主キーだけを外部キーの参照先として扱う。</summary>
public sealed class LegacyPerson
{
    public int Code { get; set; }
}
