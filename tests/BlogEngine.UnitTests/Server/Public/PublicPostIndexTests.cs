using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="PublicPostIndex"/>: the slug lookups the post and tag pages use, and the older/newer and related
/// posts shown under a post (P10, T4.11).
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

    /// <summary>Posts in the index's order (newest first), with the tags given by id.</summary>
    private static PublicPostIndex IndexOf(params (int Id, int[] TagIds)[] posts)
    {
        return new PublicPostIndex(
            [.. posts.Select((p, i) => PublicTestData.Post(p.Id, new DateOnly(2026, 9, 28).AddDays(-i),
                [.. p.TagIds.Select(t => new PublicTagLink(t, $"Tag {t}", $"tag-{t}"))]))],
            []);
    }

    /// <summary>A post in the middle has both neighbors: the newer one before it in the list, the older one after.</summary>
    [Test]
    public async Task GetNeighbors_Middle_HasNewerAndOlder()
    {
        var index = IndexOf((3, []), (2, []), (1, []));

        var neighbors = index.GetNeighbors(2);

        await Assert.That(neighbors.Newer!.Id).IsEqualTo(3);
        await Assert.That(neighbors.Older!.Id).IsEqualTo(1);
    }

    /// <summary>The newest post has no newer post, and the oldest no older one.</summary>
    [Test]
    public async Task GetNeighbors_Ends_OmitMissingSide()
    {
        var index = IndexOf((3, []), (2, []), (1, []));

        var newest = index.GetNeighbors(3);
        var oldest = index.GetNeighbors(1);

        await Assert.That(newest.Newer).IsNull();
        await Assert.That(newest.Older!.Id).IsEqualTo(2);
        await Assert.That(oldest.Newer!.Id).IsEqualTo(2);
        await Assert.That(oldest.Older).IsNull();
    }

    /// <summary>The only post, or one that isn't in the index, has no neighbors.</summary>
    [Test]
    public async Task GetNeighbors_OnlyOrUnknownPost_HasNone()
    {
        await Assert.That(IndexOf((1, [])).GetNeighbors(1)).IsEqualTo(PublicPostNeighbors.None);
        await Assert.That(IndexOf((1, [])).GetNeighbors(99)).IsEqualTo(PublicPostNeighbors.None);
    }

    /// <summary>Related posts share a tag: most shared tags first, newest first among equals, never the post itself.</summary>
    [Test]
    public async Task GetRelated_OrdersBySharedTagsThenRecency()
    {
        var index = IndexOf((6, [1]), (5, [9]), (4, [1, 2]), (3, [1]), (2, [1, 2]), (1, [1, 2]));
        var subject = index.FindPost("post-1")!;

        var related = index.GetRelated(subject, 3);

        await Assert.That(related.Select(p => p.Id)).IsEquivalentTo([4, 2, 6], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>A post without tags, or a count of zero, has no related posts.</summary>
    [Test]
    public async Task GetRelated_NoTagsOrNoCount_IsEmpty()
    {
        var index = IndexOf((2, [1]), (1, []));

        await Assert.That(index.GetRelated(index.FindPost("post-1")!, 3)).IsEmpty();
        await Assert.That(index.GetRelated(index.FindPost("post-2")!, 0)).IsEmpty();
    }
}
