using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the public query cache and its invalidation (design 11, T1.17): results are cached, and a publish,
/// update, unpublish or delete is visible on the very next request anyway.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PublicCacheTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>
    /// Each change to a post shows on the next request of a page that was already cached: publishing another
    /// post adds it, and unpublishing and trashing remove posts.
    /// </summary>
    [Test]
    public async Task PostChanges_AreVisibleImmediately_DespiteCaching()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var yearPath = PostPaths.Year(year);
        var first = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"First {token}" }, PublicTestPosts.Noon(year, 1, 1));
        using var client = IdentityTestHelper.CreateClient(factory);
        await Assert.That(await PublicTestPosts.GetOkAsync(client, yearPath)).Contains($"First {token}");

        var second = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Second {token}" }, PublicTestPosts.Noon(year, 1, 2));
        var afterPublish = await PublicTestPosts.GetOkAsync(client, yearPath);

        second.Title = $"Renamed {token}";
        await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(second.Id, second));
        var afterUpdate = await PublicTestPosts.GetOkAsync(client, yearPath);

        await PublicTestPosts.SaveAsync(factory, s => s.UnpublishAsync(first.Id, new UnpublishPostRequest()));
        var afterUnpublish = await PublicTestPosts.GetOkAsync(client, yearPath);

        await Assert.That(afterPublish).Contains($"Second {token}");
        await Assert.That(afterUpdate).Contains($"Renamed {token}");
        await Assert.That(afterUpdate).DoesNotContain($"Second {token}");
        await Assert.That(afterUnpublish).DoesNotContain($"First {token}");
        await Assert.That(afterUnpublish).Contains($"Renamed {token}");
    }

    /// <summary>
    /// A change made behind the cache's back (straight to the database) isn't seen, proving results are cached,
    /// until <see cref="CacheInvalidator"/> evicts them.
    /// </summary>
    /// <remarks>
    /// Uses its own cache and invalidator over the shared database: posts saved by tests running in parallel
    /// evict the application's cache, which would make "still cached" flaky.
    /// </remarks>
    [Test]
    public async Task Queries_AreCached_UntilEvicted()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Cached {token}", ContentMarkdown = "Before." },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 8, 8));

        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddOutputCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var invalidator = new CacheInvalidator(cache, provider.GetRequiredService<IOutputCacheStore>(), NullLogger<CacheInvalidator>.Instance);
        var queries = new PublicPostQueries(cache, invalidator, factory.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        await Assert.That((await queries.GetPostAsync(post.Slug!))!.Html).Contains("Before.");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Posts.Where(p => p.Id == post.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ContentHtml, "<p>Behind the cache.</p>"));
        }

        var stillCached = (await queries.GetPostAsync(post.Slug!))!.Html;
        await invalidator.PostChangedAsync(post.Id);
        var afterEviction = (await queries.GetPostAsync(post.Slug!))!.Html;

        await Assert.That(stillCached).Contains("Before.");
        await Assert.That(afterEviction).Contains("Behind the cache.");
    }
}
