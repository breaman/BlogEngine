using System.Globalization;

using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="BlogTimeZone"/>: the URL date is the calendar date in the blog's time zone, including
/// around local midnight and daylight saving changes.
/// </summary>
public class BlogTimeZoneTests
{
    private const string Chicago = "America/Chicago";

    /// <summary>The design's example: 9 PM Central on September 22 belongs under <c>/22/</c>, not <c>/23/</c>.</summary>
    [Test]
    public async Task ToLocalDate_UsesBlogTimeZoneNotUtc()
    {
        var ninePmCentral = new DateTimeOffset(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);

        await Assert.That(BlogTimeZone.ToLocalDate(ninePmCentral, Chicago)).IsEqualTo(new DateOnly(2026, 9, 22));
        await Assert.That(BlogTimeZone.ToLocalDate(ninePmCentral, "UTC")).IsEqualTo(new DateOnly(2026, 9, 23));
    }

    /// <summary>
    /// The local date flips exactly at local midnight: the last second before it and midnight itself, in
    /// standard time, daylight time, and on both daylight saving transition days (US 2026: March 8 and
    /// November 1).
    /// </summary>
    [Test]
    // Summer (CDT, UTC-5): midnight is 05:00Z.
    [Arguments("2026-09-23T04:59:59Z", "2026-09-22")]
    [Arguments("2026-09-23T05:00:00Z", "2026-09-23")]
    // Winter (CST, UTC-6): midnight is 06:00Z.
    [Arguments("2026-01-15T05:59:59Z", "2026-01-14")]
    [Arguments("2026-01-15T06:00:00Z", "2026-01-15")]
    // Spring forward: March 8 starts in CST and ends in CDT, so it is only 23 hours long.
    [Arguments("2026-03-08T05:59:59Z", "2026-03-07")]
    [Arguments("2026-03-08T06:00:00Z", "2026-03-08")]
    [Arguments("2026-03-08T07:59:59Z", "2026-03-08")]
    [Arguments("2026-03-08T08:00:00Z", "2026-03-08")]
    [Arguments("2026-03-09T04:59:59Z", "2026-03-08")]
    [Arguments("2026-03-09T05:00:00Z", "2026-03-09")]
    // Fall back: November 1 starts in CDT and ends in CST, so it is 25 hours long.
    [Arguments("2026-11-01T04:59:59Z", "2026-10-31")]
    [Arguments("2026-11-01T05:00:00Z", "2026-11-01")]
    [Arguments("2026-11-01T06:30:00Z", "2026-11-01")]
    [Arguments("2026-11-01T07:30:00Z", "2026-11-01")]
    [Arguments("2026-11-02T05:59:59Z", "2026-11-01")]
    [Arguments("2026-11-02T06:00:00Z", "2026-11-02")]
    public async Task ToLocalDate_FlipsAtLocalMidnight(string instant, string expectedDate)
    {
        await Assert.That(BlogTimeZone.ToLocalDate(ParseInstant(instant), Chicago))
            .IsEqualTo(DateOnly.Parse(expectedDate, CultureInfo.InvariantCulture));
    }

    /// <summary>Zones ahead of UTC, including half-hour offsets, roll over before UTC does.</summary>
    [Test]
    [Arguments("Asia/Kolkata", "2026-09-22T18:29:59Z", "2026-09-22")]
    [Arguments("Asia/Kolkata", "2026-09-22T18:30:00Z", "2026-09-23")]
    [Arguments("Pacific/Auckland", "2026-09-22T11:59:59Z", "2026-09-22")]
    [Arguments("Pacific/Auckland", "2026-09-22T12:00:00Z", "2026-09-23")]
    public async Task ToLocalDate_HandlesZonesAheadOfUtc(string timeZoneId, string instant, string expectedDate)
    {
        await Assert.That(BlogTimeZone.ToLocalDate(ParseInstant(instant), timeZoneId))
            .IsEqualTo(DateOnly.Parse(expectedDate, CultureInfo.InvariantCulture));
    }

    /// <summary>The input's own offset only identifies the instant; the blog's time zone decides the date.</summary>
    [Test]
    public async Task ToLocalDate_IgnoresInputOffset()
    {
        var sameInstantInTokyo = new DateTimeOffset(2026, 9, 23, 11, 0, 0, TimeSpan.FromHours(9));

        await Assert.That(BlogTimeZone.ToLocalDate(sameInstantInTokyo, Chicago)).IsEqualTo(new DateOnly(2026, 9, 22));
    }

    /// <summary>The <see cref="TimeZoneInfo"/> overload agrees with the id overload.</summary>
    [Test]
    public async Task ToLocalDate_AcceptsTimeZoneInfo()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Chicago);
        var instant = ParseInstant("2026-09-23T02:00:00Z");

        await Assert.That(BlogTimeZone.ToLocalDate(instant, zone)).IsEqualTo(BlogTimeZone.ToLocalDate(instant, Chicago));
    }

    /// <summary>An unknown time zone id fails loudly instead of silently falling back to UTC.</summary>
    [Test]
    public async Task ToLocalDate_ThrowsForUnknownTimeZone()
    {
        await Assert.That(() => BlogTimeZone.ToLocalDate(DateTimeOffset.UnixEpoch, "Mars/Olympus_Mons"))
            .Throws<TimeZoneNotFoundException>();
    }

    private static DateTimeOffset ParseInstant(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}