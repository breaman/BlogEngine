namespace BlogEngine.Shared.Common;

/// <summary>
/// Converts instants to dates in the blog's configured time zone (design 7.1, Q10).
/// </summary>
/// <remarks>
/// <para>
/// URL dates and archives use the blog's time zone rather than UTC: a post published at 9 PM Central on
/// September 22 belongs under <c>/22/</c>, not <c>/23/</c>.
/// </para>
/// <para>
/// Converting an instant to a local date is always unambiguous, even across daylight saving changes; only
/// the reverse (local time to instant) can hit skipped or repeated hours.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// post.PublishedDateLocal = BlogTimeZone.ToLocalDate(post.PublishedOn.Value, settings.TimeZoneId);
/// </code>
/// </example>
public static class BlogTimeZone
{
    /// <summary>The calendar date of <paramref name="instant"/> in <paramref name="timeZone"/>.</summary>
    /// <param name="instant">A point in time; its offset only identifies the instant and is otherwise ignored.</param>
    /// <param name="timeZone">The blog's time zone.</param>
    public static DateOnly ToLocalDate(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
    }

    /// <summary>The calendar date of <paramref name="instant"/> in the time zone <paramref name="timeZoneId"/>.</summary>
    /// <param name="instant">A point in time; its offset only identifies the instant and is otherwise ignored.</param>
    /// <param name="timeZoneId">An IANA (or Windows) time zone id, such as <c>America/Chicago</c>.</param>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is unknown on this host.</exception>
    public static DateOnly ToLocalDate(DateTimeOffset instant, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        return ToLocalDate(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }

    /// <summary>
    /// The wall-clock date and time of <paramref name="instant"/> in the time zone <paramref name="timeZoneId"/>, as
    /// shown by the editor's schedule picker (design 10.2).
    /// </summary>
    /// <param name="instant">A point in time.</param>
    /// <param name="timeZoneId">An IANA (or Windows) time zone id, such as <c>America/Chicago</c>.</param>
    /// <returns>A <see cref="DateTimeKind.Unspecified"/> value: a local time with no offset attached.</returns>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is unknown on this host.</exception>
    public static DateTime ToLocalDateTime(DateTimeOffset instant, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime;
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The instant at which the wall clock in <paramref name="timeZoneId"/> shows <paramref name="localDateTime"/>, for
    /// turning the schedule picker's value into a publish time (design 10.2, A9).
    /// </summary>
    /// <remarks>
    /// Local times aren't always unique. A time skipped when the clocks go forward (2:30 AM on a spring-forward day)
    /// is moved forward by the length of the gap, as a clock on the wall would read it; a time that happens twice when
    /// the clocks go back is taken the first time, in daylight time, so a scheduled post never goes live later than
    /// the author expects.
    /// </remarks>
    /// <param name="localDateTime">A wall-clock time; its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZoneId">An IANA (or Windows) time zone id, such as <c>America/Chicago</c>.</param>
    /// <returns>The instant, in UTC.</returns>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is unknown on this host.</exception>
    /// <example>
    /// <code>
    /// // 9 AM Central on October 1, 2026 (CDT, UTC-5) is 14:00 UTC.
    /// var publishOn = BlogTimeZone.FromLocalDateTime(new DateTime(2026, 10, 1, 9, 0, 0), "America/Chicago");
    /// </code>
    /// </example>
    public static DateTimeOffset FromLocalDateTime(DateTime localDateTime, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            // Skipped hour: the offset in force just before the gap carries the clock past it.
            var before = zone.GetUtcOffset(local.AddHours(-3));
            return new DateTimeOffset(local, before).ToUniversalTime();
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}