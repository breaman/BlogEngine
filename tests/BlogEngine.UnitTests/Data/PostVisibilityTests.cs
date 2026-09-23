using BlogEngine.Data.Models;
using BlogEngine.Data.Queries;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.UnitTests.Data;

/// <summary>
/// Tests the public visibility rule (design 6.3): <c>Published &amp;&amp; PublishedOn &lt;= now &amp;&amp; !IsDeleted</c>.
/// </summary>
public class PostVisibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Only published, already-live, untrashed posts are visible.</summary>
    [Test]
    [Arguments(PostStatus.Published, -60, false, true)]
    [Arguments(PostStatus.Published, 0, false, true)]
    [Arguments(PostStatus.Published, 1, false, false)]
    [Arguments(PostStatus.Published, 60 * 24, false, false)]
    [Arguments(PostStatus.Published, -60, true, false)]
    [Arguments(PostStatus.Draft, -60, false, false)]
    [Arguments(PostStatus.Draft, 60, false, false)]
    public async Task VisibleToPublic_AppliesRule(PostStatus status, int publishedMinutesFromNow, bool isDeleted, bool expectedVisible)
    {
        var post = new Post
        {
            Status = status,
            PublishedOn = Now.AddMinutes(publishedMinutesFromNow),
            IsDeleted = isDeleted
        };

        var visible = new[] { post }.AsQueryable().VisibleToPublic(new FixedTimeProvider(Now)).Any();

        await Assert.That(visible).IsEqualTo(expectedVisible);
    }

    /// <summary>A published post with no publish time (inconsistent data) is never shown.</summary>
    [Test]
    public async Task VisibleToPublic_HidesPublishedPostWithoutPublishTime()
    {
        var post = new Post { Status = PostStatus.Published, PublishedOn = null };

        await Assert.That(new[] { post }.AsQueryable().VisibleToPublic(new FixedTimeProvider(Now)).Any()).IsFalse();
    }

    /// <summary>A never-published draft is not visible.</summary>
    [Test]
    public async Task VisibleToPublic_HidesNeverPublishedDraft()
    {
        var post = new Post { Status = PostStatus.Draft, PublishedOn = null };

        await Assert.That(new[] { post }.AsQueryable().VisibleToPublic(new FixedTimeProvider(Now)).Any()).IsFalse();
    }

    /// <summary>A scheduled post becomes visible once the clock passes its publish time, with no other change.</summary>
    [Test]
    public async Task VisibleToPublic_ScheduledPostAppearsWhenTimePasses()
    {
        var scheduled = new Post { Status = PostStatus.Published, PublishedOn = Now.AddHours(1) };
        var posts = new[] { scheduled }.AsQueryable();

        await Assert.That(posts.VisibleToPublic(new FixedTimeProvider(Now)).Any()).IsFalse();
        await Assert.That(posts.VisibleToPublic(new FixedTimeProvider(Now.AddHours(1))).Any()).IsTrue();
    }

    /// <summary>The comparison is by instant, so a publish time stored with a non-UTC offset works.</summary>
    [Test]
    public async Task VisibleToPublic_ComparesInstantsNotLocalTimes()
    {
        // 11:30 at UTC-5 is 16:30 UTC, after "now" (12:00 UTC), even though 11:30 < 12:00 on the clock face.
        var post = new Post
        {
            Status = PostStatus.Published,
            PublishedOn = new DateTimeOffset(2026, 9, 22, 11, 30, 0, TimeSpan.FromHours(-5))
        };

        await Assert.That(new[] { post }.AsQueryable().VisibleToPublic(new FixedTimeProvider(Now)).Any()).IsFalse();
    }

    /// <summary>The rule translates to SQL, with the current time sent as a parameter.</summary>
    [Test]
    public async Task VisibleToPublic_TranslatesToSql()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);

        var sql = dbContext.Posts.VisibleToPublic(new FixedTimeProvider(Now)).ToQueryString();

        await Assert.That(sql).Contains("[p].[Status]");
        await Assert.That(sql).Contains("[p].[PublishedOn] <= @");
        await Assert.That(sql).Contains("[p].[IsDeleted]");
    }

    /// <summary>A clock frozen at one instant.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}