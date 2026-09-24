using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Tests the last-modified and "Updated" dates of <see cref="PublicPostSummary"/> (P15, T4.8).</summary>
public class PublicPostSummaryTests
{
    private const string Chicago = "America/Chicago";

    /// <summary>An update on a later local day is shown; one on the publish day, or none, isn't.</summary>
    [Test]
    public async Task UpdatedDateLocal_OnlyForLaterDays()
    {
        var post = PublicTestData.Post(1, new DateOnly(2026, 9, 22));

        var later = post with { LastUpdatedOn = new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero) };
        var sameDay = post with { LastUpdatedOn = new DateTimeOffset(2026, 9, 22, 20, 0, 0, TimeSpan.Zero) };

        await Assert.That(later.UpdatedDateLocal(Chicago)).IsEqualTo(new DateOnly(2026, 9, 25));
        await Assert.That(sameDay.UpdatedDateLocal(Chicago)).IsNull();
        await Assert.That(post.UpdatedDateLocal(Chicago)).IsNull();
    }

    /// <summary>The updated day is taken in the blog's time zone: 2 AM UTC on the 23rd is still the 22nd in Chicago.</summary>
    [Test]
    public async Task UpdatedDateLocal_UsesBlogTimeZone()
    {
        var post = PublicTestData.Post(1, new DateOnly(2026, 9, 21)) with { LastUpdatedOn = new DateTimeOffset(2026, 9, 23, 2, 0, 0, TimeSpan.Zero) };

        await Assert.That(post.UpdatedDateLocal(Chicago)).IsEqualTo(new DateOnly(2026, 9, 22));
    }

    /// <summary>The last-modified time is the update when it is later, and never before the publish time.</summary>
    [Test]
    public async Task LastModified_IsLaterOfPublishAndUpdate()
    {
        var post = PublicTestData.Post(1, new DateOnly(2026, 9, 22));

        await Assert.That(post.LastModified).IsEqualTo(post.PublishedOn);
        await Assert.That((post with { LastUpdatedOn = post.PublishedOn.AddDays(2) }).LastModified).IsEqualTo(post.PublishedOn.AddDays(2));
        await Assert.That((post with { LastUpdatedOn = post.PublishedOn.AddDays(-2) }).LastModified).IsEqualTo(post.PublishedOn);
    }
}
