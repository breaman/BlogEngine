using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="PublicPostIndex"/>: the slug lookups the post and tag pages use.
/// </summary>
public class PublicPostIndexTests
{
    private static readonly PublicPostIndex Index = new(
        [PublicTestData.Post(1), PublicTestData.Post(2)],
        [new PublicTag(7, "C#", "csharp", null, 2)]);

    /// <summary>Posts are found by slug, ignoring case.</summary>
    [Test]
    [Arguments("post-2")]
    [Arguments("POST-2")]
    public async Task FindPost_MatchesSlugIgnoringCase(string slug)
    {
        await Assert.That(Index.FindPost(slug)!.Id).IsEqualTo(2);
    }

    /// <summary>Tags are found by slug, ignoring case.</summary>
    [Test]
    [Arguments("csharp")]
    [Arguments("CSharp")]
    public async Task FindTag_MatchesSlugIgnoringCase(string slug)
    {
        await Assert.That(Index.FindTag(slug)!.Id).IsEqualTo(7);
    }

    /// <summary>Unknown and missing slugs find nothing.</summary>
    [Test]
    public async Task Find_UnknownSlug_ReturnsNull()
    {
        await Assert.That(Index.FindPost("post-3")).IsNull();
        await Assert.That(Index.FindPost(null)).IsNull();
        await Assert.That(Index.FindTag("dotnet")).IsNull();
        await Assert.That(PublicPostIndex.Empty.FindPost("post-1")).IsNull();
    }
}
