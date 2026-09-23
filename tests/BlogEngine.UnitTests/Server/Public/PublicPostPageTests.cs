using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="PublicPostPage"/>: slicing a list into pages and deciding which pages exist (design 7.1).
/// </summary>
public class PublicPostPageTests
{
    private static readonly PublicPostSummary[] TwelvePosts = [.. Enumerable.Range(1, 12).Select(PublicTestData.Post)];

    /// <summary>Pages hold <c>pageSize</c> posts in order, and the last page holds the rest.</summary>
    [Test]
    public async Task Create_SlicesPages()
    {
        var first = PublicPostPage.Create(TwelvePosts, 1, 10);
        var second = PublicPostPage.Create(TwelvePosts, 2, 10);

        await Assert.That(first.Posts.Select(p => p.Id)).IsEquivalentTo(Enumerable.Range(1, 10));
        await Assert.That(second.Posts.Select(p => p.Id)).IsEquivalentTo([11, 12]);
        await Assert.That(first.TotalPages).IsEqualTo(2);
        await Assert.That(first.TotalCount).IsEqualTo(12);
        await Assert.That(second.IsPageInRange).IsTrue();
    }

    /// <summary>Pages past the end, page 0 and absurdly large page numbers are empty and out of range.</summary>
    [Test]
    [Arguments(0)]
    [Arguments(-3)]
    [Arguments(3)]
    [Arguments(int.MaxValue)]
    public async Task Create_OutOfRange_IsEmpty(int page)
    {
        var result = PublicPostPage.Create(TwelvePosts, page, 10);

        await Assert.That(result.Posts).IsEmpty();
        await Assert.That(result.IsPageInRange).IsFalse();
    }

    /// <summary>An empty list still has a first page (the "no posts yet" page), but nothing after it.</summary>
    [Test]
    public async Task Create_EmptyList_HasOnlyPageOne()
    {
        await Assert.That(PublicPostPage.Create([], 1, 10).IsPageInRange).IsTrue();
        await Assert.That(PublicPostPage.Create([], 1, 10).TotalPages).IsEqualTo(1);
        await Assert.That(PublicPostPage.Create([], 2, 10).IsPageInRange).IsFalse();
    }
}
