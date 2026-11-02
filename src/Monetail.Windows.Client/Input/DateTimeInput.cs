using System.Globalization;

namespace Monetail.Windows.Client.Input;

/// <summary>画面や地域設定に依存しない、日付・時刻入力の補完と表示。</summary>
public static class DateTimeInput
{
    public static DateOnly? NormalizeDate(string text, bool allowEmpty, DateOnly today) =>
        ReadDate(NormalizeCharacters(text), today) ?? (allowEmpty ? null : today);

    public static TimeOnly? NormalizeTime(string text, bool allowEmpty) =>
        ReadTime(NormalizeCharacters(text)) ?? (allowEmpty ? null : TimeOnly.MinValue);

    public static string FormatDate(DateOnly? value) => value?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "";
    public static string FormatTime(TimeOnly? value) => value?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    private static string NormalizeCharacters(string text) => new(text.Trim().Select(character => character switch
    {
        >= '０' and <= '９' => (char)('0' + character - '０'),
        '－' => '-', '．' => '.', '／' => '/', '：' => ':',
        _ => character
    }).ToArray());

    private static bool Digits(string value, int min, int max) => value.Length >= min && value.Length <= max &&
        value.All(character => character is >= '0' and <= '9');
    private static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static DateOnly? ReadDate(string text, DateOnly today)
    {
        if (Digits(text, 1, 8))
            return text.Length switch
            {
                <= 2 => PreviousDay(Number(text), today),
                3 or 4 => PreviousMonthDay(Number(text[..^2]), Number(text[^2..]), today),
                6 => Date(NearestYear(Number(text[..2]), today.Year), Number(text[2..4]), Number(text[4..])),
                8 => Date(Number(text[..4]), Number(text[4..6]), Number(text[6..])),
                _ => null
            };
        var parts = text.Split(['-', '.', '/']);
        if (parts.Length == 2 && parts.All(part => Digits(part, 1, 2)))
            return PreviousMonthDay(Number(parts[0]), Number(parts[1]), today);
        if (parts.Length == 3 && (Digits(parts[0], 2, 2) || Digits(parts[0], 4, 4)) &&
            Digits(parts[1], 1, 2) && Digits(parts[2], 1, 2))
            return Date(parts[0].Length == 2 ? NearestYear(Number(parts[0]), today.Year) : Number(parts[0]),
                Number(parts[1]), Number(parts[2]));
        return null;
    }

    private static DateOnly? PreviousDay(int day, DateOnly today)
    {
        var first = today.Year == 1 && today.Month == 1 ? DateOnly.MinValue : today.AddMonths(-1);
        var current = Date(today.Year, today.Month, day);
        if (current is { } value && value <= today) return value;
        var previous = Date(first.Year, first.Month, day);
        return previous is { } prior && prior >= first && prior <= today ? prior : null;
    }

    private static DateOnly? PreviousMonthDay(int month, int day, DateOnly today)
    {
        var first = today.Year == 1 ? DateOnly.MinValue : today.AddYears(-1);
        for (var year = today.Year; year >= first.Year; year--)
            if (Date(year, month, day) is { } value && value >= first && value <= today) return value;
        return null;
    }

    private static int NearestYear(int year, int current)
    {
        var sameCentury = current / 100 * 100 + year;
        // 年の距離を先に比較し、うるう日の存在を理由に他の世紀へ移さない。
        return new[] { sameCentury - 100, sameCentury, sameCentury + 100 }
            .Where(candidate => candidate is >= 1 and <= 9999)
            .OrderBy(candidate => Math.Abs(candidate - current)).ThenBy(candidate => candidate).First();
    }

    private static DateOnly? Date(int year, int month, int day) => year is >= 1 and <= 9999 && month is >= 1 and <= 12 &&
        day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

    private static TimeOnly? ReadTime(string text)
    {
        int hour, minute = 0, second = 0;
        if (Digits(text, 1, 6))
        {
            if (text.Length <= 2) hour = Number(text);
            else if (text.Length <= 4) { hour = Number(text[..^2]); minute = Number(text[^2..]); }
            else { hour = Number(text[..^4]); minute = Number(text[^4..^2]); second = Number(text[^2..]); }
        }
        else
        {
            var parts = text.Split(':');
            if (parts.Length is not (2 or 3) || !parts.All(part => Digits(part, 1, 2))) return null;
            hour = Number(parts[0]); minute = Number(parts[1]);
            if (parts.Length == 3) second = Number(parts[2]);
        }
        return hour <= 23 && minute <= 59 && second <= 59 ? new TimeOnly(hour, minute, second) : null;
    }
}
