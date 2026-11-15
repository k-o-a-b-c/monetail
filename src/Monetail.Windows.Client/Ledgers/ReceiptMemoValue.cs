namespace Monetail.Windows.Client.Ledgers;

/// <summary>メモは空文字と未設定を区別せず、空白だけの入力はそのまま保持する。</summary>
public static class ReceiptMemoValue
{
    public static string? Normalize(string? value) => value is "" ? null : value;
}
