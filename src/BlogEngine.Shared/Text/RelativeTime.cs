using System.Globalization;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Formats how long ago something happened in plain English ("just now", "5 minutes ago", "3 days ago"), for comment
/// dates on the post page (design 14.2, T3.6) and in the moderation queue (design 8.4).
/// </summary>
/// <remarks>
/// Deliberately coarse: exact timestamps belong in a <c>&lt;time&gt;</c> element's <c>datetime</c> and <c>title</c>.
/// Past about a year it reports years, and a timestamp in the future (a clock skew) reads as "just now".
/// </remarks>
/// <example>
/// <code>
/// RelativeTime.Format(comment.CreatedOn, timeProvider.GetUtcNow()); // "2 hours ago"
/// </code>
/// </example>
public static class RelativeTime
{
    /// <summary>Describes how long before <paramref name="now"/> the moment <paramref name="value"/> was.</summary>
    public static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now - value;

        return elapsed switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => Plural((int)elapsed.TotalMinutes, "minute"),
            { TotalHours: < 24 } => Plural((int)elapsed.TotalHours, "hour"),
            { TotalDays: < 30 } => Plural((int)elapsed.TotalDays, "day"),
            { TotalDays: < 365 } => Plural(Math.Max(1, (int)(elapsed.TotalDays / 30.44)), "month"),
            _ => Plural((int)(elapsed.TotalDays / 365.25), "year")
        };
    }

    private static string Plural(int count, string unit)
    {
        return count == 1
            ? $"1 {unit} ago"
            : string.Create(CultureInfo.InvariantCulture, $"{count} {unit}s ago");
    }
}