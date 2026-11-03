namespace Monetail.Api.Authentication;

/// <summary>アカウントの新規登録を許可する方式。家計簿への参加権限とは独立する。</summary>
public enum RegistrationMode
{
    InvitationOnly,
    Public
}

/// <summary>通常のAPI起動時に確定し、稼働中は変更しないアカウント登録設定。</summary>
public sealed record RegistrationSettings(RegistrationMode Mode)
{
    /// <summary>未指定は安全側の招待制とし、不正な明示値は起動時に拒否する。</summary>
    public static RegistrationSettings Read(IConfiguration configuration) =>
        configuration["Registration:Mode"] switch
        {
            null or "InvitationOnly" => new(RegistrationMode.InvitationOnly),
            "Public" => new(RegistrationMode.Public),
            _ => throw new InvalidOperationException(
                "Registration:ModeにはInvitationOnlyまたはPublicを指定してください。")
        };
}
