using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// A year, month or day of the post archives (design 7.1, P2, P4): <c>/posts/{yyyy}</c>,
/// <c>/posts/{yyyy}/{mm}</c> and <c>/posts/{yyyy}/{mm}/{dd}</c>.
/// </summary>
/// <remarks>
/// Periods are matched against <see cref="PublicPostSummary.PublishedDateLocal"/>, the publish date in the
/// blog's time zone, so the archives agree with the dates in post URLs.
/// </remarks>
/// <example>
/// <code>
/// if (ArchivePeriod.TryCreate(2026, 9, null) is { } september)
/// {
///     var posts = index.Posts.Where(p => september.Contains(p.PublishedDateLocal));
/// }
/// </code>
/// </example>
public sealed record ArchivePeriod
{
    /// <summary>Earliest year the archive routes accept.</summary>
    public const int MinYear = 1900;

    /// <summary>Latest year the archive routes accept.</summary>
    public const int MaxYear = 9999;

    private ArchivePeriod(int year, int? month, int? day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    /// <summary>The year.</summary>
    public int Year { get; }

    /// <summary>The month (1–12), or <see langword="null"/> for a year archive.</summary>
    public int? Month { get; }

    /// <summary>The day of the month, or <see langword="null"/> for a year or month archive.</summary>
    public int? Day { get; }

    /// <summary>
    /// The period for the route values, or <see langword="null"/> when they don't name a real date (for
    /// example February 30), which the archive page answers with a 404.
    /// </summary>
    /// <param name="year">Year from the URL.</param>
    /// <param name="month">Month from the URL; <see langword="null"/> for a year archive.</param>
    /// <param name="day">Day from the URL; <see langword="null"/> for a year or month archive. Requires a month.</param>
    public static ArchivePeriod? TryCreate(int year, int? month, int? day)
    {
        var isValid = year is >= MinYear and <= MaxYear
            && (month is null || month is >= 1 and <= 12)
            && (day is null || (month is not null && day >= 1 && day <= DateTime.DaysInMonth(year, month.Value)));

        return isValid ? new ArchivePeriod(year, month, day) : null;
    }

    /// <summary>The first day of the period.</summary>
    public DateOnly Start => new(Year, Month ?? 1, Day ?? 1);

    /// <summary>The canonical path of the archive, always zero-padded.</summary>
    public string Path => (Month, Day) switch
    {
        (null, _) => PostPaths.Year(Year),
        ({ } month, null) => PostPaths.Month(Year, month),
        _ => PostPaths.Day(Start)
    };

    /// <summary>The enclosing period (the month of a day, the year of a month), or <see langword="null"/> for a year.</summary>
    public ArchivePeriod? Parent => (Month, Day) switch
    {
        (null, _) => null,
        (_, null) => new ArchivePeriod(Year, null, null),
        _ => new ArchivePeriod(Year, Month, null)
    };

    /// <summary>Whether <paramref name="date"/> falls inside the period.</summary>
    public bool Contains(DateOnly date)
    {
        return date.Year == Year
            && (Month is null || date.Month == Month)
            && (Day is null || date.Day == Day);
    }
}
