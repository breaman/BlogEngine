using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="PostPaths"/>: the canonical post and archive URL forms (design 7.1, 16).
/// </summary>
public class PostPathsTests
{
    /// <summary>Month and day are always zero-padded.</summary>
    [Test]
    [Arguments(2026, 9, 2, "/posts/2026/09/02/hello")]
    [Arguments(2026, 12, 31, "/posts/2026/12/31/hello")]
    public async Task Post_ZeroPadsDate(int year, int month, int day, string expected)
    {
        await Assert.That(PostPaths.Post(new DateOnly(year, month, day), "hello")).IsEqualTo(expected);
    }

    /// <summary>Archive paths are zero-padded at every level.</summary>
    [Test]
    public async Task Archives_ZeroPad()
    {
        await Assert.That(PostPaths.Year(2026)).IsEqualTo("/posts/2026");
        await Assert.That(PostPaths.Month(2026, 9)).IsEqualTo("/posts/2026/09");
        await Assert.That(PostPaths.Day(new DateOnly(2026, 9, 2))).IsEqualTo("/posts/2026/09/02");
    }

    /// <summary>Page 1 (and anything below it) has no query string; later pages do.</summary>
    [Test]
    [Arguments(0, "/posts")]
    [Arguments(1, "/posts")]
    [Arguments(2, "/posts?page=2")]
    [Arguments(12, "/posts?page=12")]
    public async Task WithPage_OmitsFirstPage(int page, string expected)
    {
        await Assert.That(PostPaths.WithPage(PostPaths.Index, page)).IsEqualTo(expected);
    }
}
