namespace Monetail.Windows.Client.Authentication;

/// <summary>別ウィンドウ等で保存認証が変わり、現在の画面の本人を保証できないことを示す。</summary>
public sealed class AuthenticationContextChangedException : Exception
{
    public AuthenticationContextChangedException() : base("保存されたログイン状態が変更されました。もう一度ログインしてください。") { }
}
