namespace Monetail.Windows.Client.Authentication;

/// <summary>サーバーが保存済みの認証または認証要求を拒否したことを示す。</summary>
public sealed class AuthenticationRejectedException : Exception
{
    public AuthenticationRejectedException() : base("ログインを継続できません。もう一度ログインしてください。") { }
}
