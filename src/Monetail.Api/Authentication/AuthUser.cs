using Microsoft.AspNetCore.Identity;

namespace Monetail.Api.Authentication;

/// <summary>ログイン可能な人物の認証情報を管理する。</summary>
public sealed class AuthUser : IdentityUser
{
    /// <summary>パスキー登録を完了したアカウントの利用可否を保持する。</summary>
    public bool IsEnabled { get; set; }
}

/// <summary>認証アカウントと既存の人物コードを対応付ける。</summary>
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
