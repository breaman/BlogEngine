using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Builds public read models for tests.</summary>
internal static class PublicTestData
{
    /// <summary>A visible post numbered <paramref name="id"/>, published on September 22, 2026 unless given a date.</summary>
    public static PublicPostSummary Post(int id)
    {
        return Post(id, new DateOnly(2026, 9, 22));
    }

    /// <summary>A visible post numbered <paramref name="id"/>, published at noon UTC on <paramref name="date"/>.</summary>
    public static PublicPostSummary Post(int id, DateOnly date, params PublicTagLink[] tags)
    {
        return new PublicPostSummary(id, $"Post {id}", $"post-{id}", $"Summary {id}.",
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), date, LastUpdatedOn: null,
            ReadingMinutes: 3, IsFeatured: false, tags);
    }

    /// <summary>A post with its content.</summary>
    public static PublicPostContent Content(PublicPostSummary post, string html)
    {
        return new PublicPostContent(post, html, HasCodeBlocks: false, MetaTitle: null, MetaDescription: null);
    }
}