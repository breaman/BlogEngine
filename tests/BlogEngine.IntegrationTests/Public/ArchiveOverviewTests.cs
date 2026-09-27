using System.Text.RegularExpressions;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the archive overview at <c>/archive</c> (design 7.1, P11, T4.12): years and months with post counts that match
/// the archive pages they link to, drafts not counted.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public partial class ArchiveOverviewTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A year's months are listed with their counts, and each count matches its month's archive page.</summary>
    [Test]
    public async Task Counts_MatchArchivePages()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        foreach (var (month, day) in new[] { (3, 1), (3, 2), (3, 3), (7, 9) })
        {
            await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Archived {token} {month}-{day}" }, PublicTestPosts.Noon(year, month, day));
        }

        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Unpublished {token}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, SitePaths.Archive);
        var march = await PublicTestPosts.GetOkAsync(client, PostPaths.Month(year, 3));
        var july = await PublicTestPosts.GetOkAsync(client, PostPaths.Month(year, 7));

        await Assert.That(html).Contains($"<a class=\"text-decoration-none\" href=\"/posts/{year}\">{year}</a>");
        await Assert.That(MonthCount(html, year, 3)).IsEqualTo(3);
        await Assert.That(MonthCount(html, year, 7)).IsEqualTo(1);
        await Assert.That(YearCount(html, year)).IsEqualTo(4);
        await Assert.That(PostCard().Count(march)).IsEqualTo(3);
        await Assert.That(PostCard().Count(july)).IsEqualTo(1);
        // Newest first: July comes before March.
        await Assert.That(html.IndexOf($"/posts/{year}/07", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf($"/posts/{year}/03", StringComparison.Ordinal));
    }

    /// <summary>The count shown next to a month link.</summary>
    private static int MonthCount(string html, int year, int month)
    {
        var match = Regex.Match(html, $@"href=""/posts/{year}/{month:D2}"">\s*\w+ <span class=""text-body-secondary"">\((\d+)\)</span>");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

    /// <summary>The count in a year's badge.</summary>
    private static int YearCount(string html, int year)
    {
        var match = Regex.Match(html, $@"href=""/posts/{year}"">{year}</a>\s*<span[^>]*>(\d+) posts?</span>");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

    [GeneratedRegex("class=\"post-card")]
    private static partial Regex PostCard();
}