using System.Globalization;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>13日後の地域別期限と、午前2時の欠落・重複を固定値で検証する。</summary>
public class SessionLifetimeTests
{
    [Theory]
    [InlineData("2026-10-01T11:00:00Z", "Asia/Tokyo", "2026-10-14T17:00:00Z")]
    [InlineData("2026-09-30T16:00:00Z", "Asia/Tokyo", "2026-10-13T17:00:00Z")]
    [InlineData("2026-09-30T21:00:00Z", "Asia/Tokyo", "2026-10-14T17:00:00Z")]
    [InlineData("2026-10-01T01:59:59Z", "Etc/UTC", "2026-10-14T02:00:00Z")]
    [InlineData("2026-10-01T02:00:00Z", "Etc/UTC", "2026-10-15T02:00:00Z")]
    [InlineData("2026-10-01T02:00:01Z", "Etc/UTC", "2026-10-15T02:00:00Z")]
    [InlineData("2026-02-23T05:30:00Z", "America/New_York", "2026-03-08T07:00:00Z")]
    [InlineData("2026-10-11T23:30:00Z", "Europe/Berlin", "2026-10-25T00:00:00Z")]
    [InlineData("2026-10-12T00:30:00Z", "Europe/Berlin", "2026-10-26T01:00:00Z")]
    [InlineData("2026-09-20T14:00:00Z", "Australia/Lord_Howe", "2026-10-03T15:30:00Z")]
    public void ExpiryIsTheFirstValidCandidateStrictlyAfterThirteenDays(string login, string zone, string expected)
    {
        var actual = SessionLifetime.Calculate(DateTimeOffset.Parse(login, CultureInfo.InvariantCulture),
            TimeZoneInfo.FindSystemTimeZoneById(zone));
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), actual);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_TimeZone")]
    [InlineData("Tokyo Standard Time")]
    public void MissingUnsupportedOrNonIanaZoneUsesTheServerZone(string? id)
    {
        Assert.Equal(TimeZoneInfo.Local, SessionLifetime.ResolveTimeZone(id));
        Assert.Equal("Asia/Tokyo", SessionLifetime.ResolveTimeZone("Asia/Tokyo").Id);
        Assert.Equal("UTC", SessionLifetime.ResolveTimeZone("UTC").Id);
    }
}
