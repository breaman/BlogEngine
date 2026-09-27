using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// The archive overview at <c>/archive</c> (design 7.1, P11): every year with visible posts, newest first, and within
/// each year its months with their post counts.
/// </summary>
/// <remarks>
/// Built from <see cref="PublicPostSummary.PublishedDateLocal"/>, the same local date the year and month archives
/// filter on, so every count matches the archive page it links to.
/// </remarks>
/// <example>
/// <code>
/// foreach (var year in ArchiveOverview.From(index.Posts))
/// {
///     Console.WriteLine($"{year.Year}: {year.PostCount} posts in {year.Months.Count} months");
/// }
/// </code>
/// </example>
public static class ArchiveOverview
{
    /// <summary>Groups <paramref name="posts"/> by local year and month, newest first.</summary>
    public static IReadOnlyList<ArchiveYear> From(IEnumerable<PublicPostSummary> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);

        return
        [
            .. posts
                .GroupBy(p => p.PublishedDateLocal.Year)
                .OrderByDescending(year => year.Key)
                .Select(year => new ArchiveYear(
                    year.Key,
                    year.Count(),
                    [
                        .. year
                            .GroupBy(p => p.PublishedDateLocal.Month)
                            .OrderByDescending(month => month.Key)
                            .Select(month => new ArchiveMonth(year.Key, month.Key, month.Count()))
                    ]))
        ];
    }
}

/// <summary>One year of the archive overview.</summary>
/// <param name="Year">The year.</param>
/// <param name="PostCount">Visible posts published that year.</param>
/// <param name="Months">The months with posts, newest first.</param>
public sealed record ArchiveYear(int Year, int PostCount, IReadOnlyList<ArchiveMonth> Months)
{
    /// <summary>The year archive, <c>/posts/{yyyy}</c>.</summary>
    public string Path => PostPaths.Year(Year);
}

/// <summary>One month of the archive overview.</summary>
/// <param name="Year">The year.</param>
/// <param name="Month">The month, 1–12.</param>
/// <param name="PostCount">Visible posts published that month.</param>
public sealed record ArchiveMonth(int Year, int Month, int PostCount)
{
    /// <summary>The month archive, <c>/posts/{yyyy}/{mm}</c>.</summary>
    public string Path => PostPaths.Month(Year, Month);

    /// <summary>The month's name, such as <c>September</c>.</summary>
    public string Name => PublicDates.MonthName(Month);
}