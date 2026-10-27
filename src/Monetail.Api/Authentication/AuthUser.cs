using Microsoft.AspNetCore.Identity;

namespace Monetail.Api.Authentication;

/// <summary>ログイン用アカウントの情報を保持する。</summary>
public sealed class AuthUser : IdentityUser
{
    /// <summary>初回登録で受け付ける表示名の最大文字数を定める。</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>アカウント自身の表示名を保持する。</summary>
    public string? DisplayName { get; set; }

    /// <summary>パスキー登録を完了したアカウントの利用可否を保持する。</summary>
    public bool IsEnabled { get; set; }
}
