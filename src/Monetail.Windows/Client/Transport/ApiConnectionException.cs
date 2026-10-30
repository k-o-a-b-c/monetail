namespace Monetail.Windows.Client.Transport;

/// <summary>通信障害と不正な応答。保存済み認証の失効とは区別する。</summary>
public sealed class ApiConnectionException : Exception
{
    public ApiConnectionException() : base("サーバーに接続できません。接続先と通信状態を確認してください。") { }
}
