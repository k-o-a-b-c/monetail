namespace Monetail.Api.Features.Monetail.Transfers;

internal static class TransferSearchText
{
    internal static string Normalize(string text)
    {
        // 英数字だけを半角大文字へ揃える。かな・記号・互換文字は変換しない。
        var characters = text.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var value = characters[i];
            if (value is >= '\uff10' and <= '\uff19' or >= '\uff21' and <= '\uff3a' or >= '\uff41' and <= '\uff5a') value = (char)(value - 0xfee0);
            if (value is >= 'a' and <= 'z') value = (char)(value - ('a' - 'A'));
            characters[i] = value;
        }
        return new string(characters);
    }
}
