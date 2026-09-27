using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests scheduled publishing on the public side (design 6.3, 11, T4.1): a scheduled post stays hidden, and appears once
/// its time passes and <see cref="ScheduledPublishWatcher"/> has evicted the cached lists.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class ScheduledPublishingTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>
    /// With a fake clock: the post is hidden while scheduled, still hidden once its time has passed but before the watcher
    /// runs (the cached index predates it), and visible after the watcher's check.
    /// </summary>
    /// <remarks>
    /// Uses its own cache, invalidator and watcher over the shared database, so evictions by tests running in parallel
    /// can't make the result flaky, and the clock can be moved without touching the application.
    /// </remarks>
    [Test]
    public async Task ScheduledPost_AppearsOnceItsTimePasses()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddOutputCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var invalidator = new CacheInvalidator(cache, provider.GetRequiredService<IOutputCacheStore>(), NullLogger<CacheInvalidator>.Instance);
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var queries = new PublicPostQueries(cache, invalidator, scopeFactory, clock);
        using var watcher = new ScheduledPublishWatcher(scopeFactory, invalidator, clock, NullLogger<ScheduledPublishWatcher>.Instance);

        // The application's own clock is real, so "a day from now" schedules the post there too.
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Scheduled {PublicTestPosts.Token()}" },
            DateTimeOffset.UtcNow.AddDays(1));

        var whileScheduled = await queries.GetPostAsync(post.Slug!);
        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        var beforeWatcher = await queries.GetPostAsync(post.Slug!);
        var wentLive = await watcher.CheckAsync();
        var afterWatcher = await queries.GetPostAsync(post.Slug!);

        await Assert.That(whileScheduled).IsNull();
        await Assert.That(beforeWatcher).IsNull();
        await Assert.That(wentLive).Contains(post.Id);
        await Assert.That(afterWatcher).IsNotNull();
        await Assert.That(afterWatcher!.Post.Path).IsEqualTo(post.PublicPath);
    }

    /// <summary>A check only reports posts that went live since the previous one.</summary>
    [Test]
    public async Task Watcher_ReportsEachPostOnce()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var invalidator = factory.Services.GetRequiredService<CacheInvalidator>();
        using var watcher = new ScheduledPublishWatcher(factory.Services.GetRequiredService<IServiceScopeFactory>(), invalidator, clock,
            NullLogger<ScheduledPublishWatcher>.Instance);
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Once {PublicTestPosts.Token()}" },
            DateTimeOffset.UtcNow.AddHours(2));

        var tooEarly = await watcher.CheckAsync();
        clock.Advance(TimeSpan.FromHours(3));
        var first = await watcher.CheckAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await watcher.CheckAsync();

        await Assert.That(tooEarly).DoesNotContain(post.Id);
        await Assert.That(first).Contains(post.Id);
        await Assert.That(second).DoesNotContain(post.Id);
    }

    /// <summary>A scheduled post's URL is a 404 over HTTP, like a draft's.</summary>
    [Test]
    public async Task ScheduledPost_Returns404()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Not yet {PublicTestPosts.Token()}" },
            DateTimeOffset.UtcNow.AddDays(1));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(post.PublicPath);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.NotFound);
        await Assert.That(post.PublicPath).StartsWith(PostPaths.Year(post.PublishedDateLocal!.Value.Year));
    }
}