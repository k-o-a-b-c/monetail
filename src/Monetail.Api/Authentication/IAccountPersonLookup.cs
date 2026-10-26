namespace Monetail.Api.Authentication;

/// <summary>認証アカウントに対応する人物のコードと表示名を保持する。</summary>
public sealed record AccountPerson(int UserCode, string DisplayName);

/// <summary>アプリ固有の人物照合を、パスキー・セッションの処理から切り離す。</summary>
public interface IAccountPersonLookup
{
    /// <summary>現在存在する対応人物を取得し、対応または人物がなければnullを返す。</summary>
    Task<AccountPerson?> FindAsync(string authUserId, CancellationToken token);
}
