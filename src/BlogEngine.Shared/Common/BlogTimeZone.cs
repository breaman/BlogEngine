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
}