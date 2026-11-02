using System.Globalization;
using Monetail.Windows.Client.Input;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class DateTimeInputTests
{
    [Theory]
    [InlineData("2026-11-02", "2", "2026/11/02")]
    [InlineData("2026-11-02", "02", "2026/11/02")]
    [InlineData("2026-11-02", "11", "2026/10/11")]
    [InlineData("2026-04-01", "31", "2026/03/31")]
    [InlineData("2026-03-01", "31", "")]
    [InlineData("2024-03-01", "30", "")]
    [InlineData("2026-11-02", "111", "2026/01/11")]
    [InlineData("2026-11-02", "1011", "2026/10/11")]
    [InlineData("2026-11-02", "12/25", "2025/12/25")]
    [InlineData("2028-03-01", "229", "2028/02/29")]
    [InlineData("2029-02-28", "0229", "2028/02/29")]
    [InlineData("2029-03-01", "2/29", "")]
    [InlineData("2028-02-01", "2/29", "")]
    [InlineData("2026-11-02", "2/29", "")]
    [InlineData("2026-11-02", "960401", "1996/04/01")]
    [InlineData("2026-11-02", "261201", "2026/12/01")]
    [InlineData("2026-11-02", "270401", "2027/04/01")]
    [InlineData("2026-11-02", "000401", "2000/04/01")]
    [InlineData("2026-11-02", "760401", "1976/04/01")]
    [InlineData("2100-01-01", "260401", "2126/04/01")]
    [InlineData("2090-01-01", "000229", "")]
    [InlineData("2026-11-02", "20261231", "2026/12/31")]
    [InlineData("2026-11-02", "20240229", "2024/02/29")]
    [InlineData("2026-11-02", "00010101", "0001/01/01")]
    [InlineData("2026-11-02", " ２０２６－１１／２ ", "2026/11/02")]
    [InlineData("2026-11-02", "26.12.1", "2026/12/01")]
    [InlineData("2026-11-02", "11．2", "2026/11/02")]
    [InlineData("0001-01-01", "31", "")]
    [InlineData("0001-01-01", "1/1", "0001/01/01")]
    public void DateCompletionHonorsCalendarWindowsAndNearestYear(string reference, string text, string expected)
    {
        var today = DateOnly.ParseExact(reference, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var actual = DateTimeInput.NormalizeDate(text, true, today);
        Assert.Equal(expected, DateTimeInput.FormatDate(actual));
        Assert.Equal(actual ?? today, DateTimeInput.NormalizeDate(text, false, today));
    }

    [Theory]
    [InlineData("")]
    [InlineData("　 ")]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("26111")]
    [InlineData("2026111")]
    [InlineData("202611021")]
    [InlineData("20260229")]
    [InlineData("2026/4/31")]
    [InlineData("2026/13/1")]
    [InlineData("0000/1/1")]
    [InlineData("2026年11月2日")]
    [InlineData("2026//11/2")]
    [InlineData("2026/11/2/")]
    [InlineData("2026/11/2/3")]
    [InlineData("2026/ 11/2")]
    [InlineData("1/11/2")]
    [InlineData("026/11/2")]
    [InlineData("²⁰²⁶/11/2")]
    public void InvalidDatesRespectOptionalOrRequiredFields(string input)
    {
        var today = new DateOnly(2026, 11, 2);
        Assert.Null(DateTimeInput.NormalizeDate(input, true, today));
        Assert.Equal(today, DateTimeInput.NormalizeDate(input, false, today));
    }

    [Theory]
    [InlineData("9", "09:00:00")]
    [InlineData("09", "09:00:00")]
    [InlineData("915", "09:15:00")]
    [InlineData("0915", "09:15:00")]
    [InlineData("91530", "09:15:30")]
    [InlineData("091530", "09:15:30")]
    [InlineData("9:5", "09:05:00")]
    [InlineData("9:5:3", "09:05:03")]
    [InlineData("9:15:30", "09:15:30")]
    [InlineData(" ２３：５９：５９　", "23:59:59")]
    [InlineData("000000", "00:00:00")]
    [InlineData("0", "00:00:00")]
    [InlineData("235959", "23:59:59")]
    public void TimeCompletionPreservesMidnightAndPadsOmittedParts(string input, string expected)
    {
        var value = DateTimeInput.NormalizeTime(input, true);
        Assert.NotNull(value);
        Assert.Equal(expected, DateTimeInput.FormatTime(value));
        Assert.Equal(value, DateTimeInput.NormalizeTime(input, false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("　")]
    [InlineData("24")]
    [InlineData("2400")]
    [InlineData("24:00")]
    [InlineData("23:59:60")]
    [InlineData("1260")]
    [InlineData("9:005")]
    [InlineData("1234567")]
    [InlineData("9::15")]
    [InlineData("9:15:")]
    [InlineData("9:15:30:1")]
    [InlineData("9: 15")]
    [InlineData("9.15")]
    [InlineData("9時15分")]
    [InlineData("-1")]
    [InlineData("9 PM")]
    public void InvalidTimesRespectOptionalOrRequiredFields(string input)
    {
        Assert.Null(DateTimeInput.NormalizeTime(input, true));
        Assert.Equal(TimeOnly.MinValue, DateTimeInput.NormalizeTime(input, false));
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void ParsingAndFormattingDoNotDependOnCurrentCulture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("2026/11/02", DateTimeInput.FormatDate(DateTimeInput.NormalizeDate("2026.11/2", false, new(2026, 11, 2))));
            Assert.Equal("09:05:03", DateTimeInput.FormatTime(DateTimeInput.NormalizeTime("9:5:3", false)));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
}
