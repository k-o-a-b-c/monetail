namespace Monetail.Api.Authentication;

/// <summary>端末の地域で13日経過後に迎える午前2時を、固定UTC期限にする。</summary>
public static class SessionLifetime
{
    /// <summary>ブラウザーのIANA地域を解決し、取得不能時はサーバーの地域を使う。</summary>
    public static TimeZoneInfo ResolveTimeZone(string? id) => id is { Length: > 0 and <= 128 } &&
        TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone : TimeZoneInfo.Local;

    /// <summary>13日後を超える最初の午前2時を、夏時間の欠落・重複を補正して求める。</summary>
    public static DateTimeOffset Calculate(DateTimeOffset loginUtc, TimeZoneInfo zone)
    {
        var baseline = loginUtc.ToUniversalTime().AddDays(13);
        var date = TimeZoneInfo.ConvertTime(baseline, zone).Date;
        while (true)
        {
            var local = DateTime.SpecifyKind(date.AddHours(2), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
            {
                var invalid = local.Ticks;
                do { local = local.AddHours(1); } while (zone.IsInvalidTime(local));
                var valid = local.Ticks;
                while (valid - invalid > 1)
                {
                    var middle = invalid + (valid - invalid) / 2;
                    if (zone.IsInvalidTime(new DateTime(middle, DateTimeKind.Unspecified))) invalid = middle;
                    else valid = middle;
                }
                local = new DateTime(valid, DateTimeKind.Unspecified);
            }
            var candidate = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
                : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            if (candidate > baseline) return candidate;
            date = date.AddDays(1);
        }
    }
}
