using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="TagPaths"/>: the tag page and tag feed URLs (design 7.1).
/// </summary>
public class TagPathsTests
{
    /// <summary>The tag page and its feed hang off <c>/tags/{slug}</c>.</summary>
    [Test]
    public async Task Paths_UseSlug()
    {
        await Assert.That(TagPaths.Tag("csharp")).IsEqualTo("/tags/csharp");
        await Assert.That(TagPaths.Feed("csharp")).IsEqualTo("/tags/csharp/feed.xml");
    }

    /// <summary>A blank slug is a programming error.</summary>
    [Test]
    public async Task Tag_BlankSlug_Throws()
    {
        await Assert.That(() => TagPaths.Tag(" ")).Throws<ArgumentException>();
    }
}
