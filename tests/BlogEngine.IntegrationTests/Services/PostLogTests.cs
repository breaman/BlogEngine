using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services;
using BlogEngine.Shared.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests the post log events (design 18, T4.27): <c>PostPublished</c> and <c>PostUnpublished</c> are structured events
/// carrying the post, and whether it was scheduled; the trash writes <c>PostTrashed</c>, <c>PostRestored</c> and
/// <c>PostsPurged</c>.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PostLogTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Publishing now, scheduling and unpublishing each log their event with the post's id and slug.</summary>
    [Test]
    public async Task PublishScheduleAndUnpublish_WriteStructuredEvents()
    {
        var logger = new CapturingLogger<ServerPostAdminService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = ActivatorUtilities.CreateInstance<ServerPostAdminService>(scope.ServiceProvider, logger);
        var draft = Saved(await posts.CreateAsync(new PostEditDto { Title = $"Logged {PublicTestPosts.Token()}" }));
        var later = DateTimeOffset.UtcNow.AddDays(10);

        var published = Saved(await posts.PublishAsync(draft.Id, new PublishPostRequest()));
        await posts.UnpublishAsync(draft.Id, new UnpublishPostRequest());
        await posts.PublishAsync(draft.Id, new PublishPostRequest { PublishOn = later });
        await posts.UnpublishAsync(draft.Id, new UnpublishPostRequest());

        var publishedEvents = logger.Entries.Where(e => e.EventId.Name == "PostPublished").ToList();
        var unpublishedEvents = logger.Entries.Where(e => e.EventId.Name == "PostUnpublished").ToList();
        await Assert.That(publishedEvents.Count).IsEqualTo(2);
        await Assert.That(unpublishedEvents.Count).IsEqualTo(2);
        await Assert.That(publishedEvents[0].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(publishedEvents[0].EventId.Id).IsEqualTo(1001);
        await Assert.That(publishedEvents[0].Properties["PostId"]).IsEqualTo(draft.Id);
        await Assert.That(publishedEvents[0].Properties["Slug"]).IsEqualTo(published.Slug);
        await Assert.That(publishedEvents[0].Properties["PublishedOn"]).IsEqualTo(published.PublishedOn!.Value);
        await Assert.That((bool?)publishedEvents[0].Properties["IsScheduled"]).IsFalse();
        await Assert.That((bool?)publishedEvents[1].Properties["IsScheduled"]).IsTrue();
        await Assert.That(unpublishedEvents[0].EventId.Id).IsEqualTo(1002);
        await Assert.That(unpublishedEvents[0].Properties["PostId"]).IsEqualTo(draft.Id);
        await Assert.That((bool?)unpublishedEvents[0].Properties["WasScheduled"]).IsFalse();
        await Assert.That((bool?)unpublishedEvents[1].Properties["WasScheduled"]).IsTrue();
    }

    /// <summary>Unpublishing a draft changes nothing, so it logs nothing.</summary>
    [Test]
    public async Task UnpublishDraft_LogsNothing()
    {
        var logger = new CapturingLogger<ServerPostAdminService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = ActivatorUtilities.CreateInstance<ServerPostAdminService>(scope.ServiceProvider, logger);
        var draft = Saved(await posts.CreateAsync(new PostEditDto { Title = $"Quiet {PublicTestPosts.Token()}" }));

        await posts.UnpublishAsync(draft.Id, new UnpublishPostRequest());

        await Assert.That(logger.Entries.Any(e => e.EventId.Name == "PostUnpublished")).IsFalse();
    }

    /// <summary>Trashing, restoring and deleting permanently each log their event.</summary>
    [Test]
    public async Task Trash_WritesStructuredEvents()
    {
        var logger = new CapturingLogger<ServerPostAdminService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = ActivatorUtilities.CreateInstance<ServerPostAdminService>(scope.ServiceProvider, logger);
        var draft = Saved(await posts.CreateAsync(new PostEditDto { Title = $"Binned {PublicTestPosts.Token()}" }));

        await posts.DeleteAsync(draft.Id);
        await posts.RestoreAsync(draft.Id);
        await posts.DeleteAsync(draft.Id);
        await posts.DeletePermanentlyAsync(draft.Id);

        var trashed = logger.Entries.Where(e => e.EventId.Name == "PostTrashed").ToList();
        var restored = logger.Entries.Single(e => e.EventId.Name == "PostRestored");
        var purged = logger.Entries.Single(e => e.EventId.Name == "PostsPurged");
        await Assert.That(trashed.Count).IsEqualTo(2);
        await Assert.That(trashed[0].Properties["PostId"]).IsEqualTo(draft.Id);
        await Assert.That((bool?)trashed[0].Properties["WasPublished"]).IsFalse();
        await Assert.That(restored.Properties["PostId"]).IsEqualTo(draft.Id);
        await Assert.That(purged.Properties["Count"]).IsEqualTo(1);
        await Assert.That(purged.Properties["PostIds"]).IsEqualTo(draft.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static PostEditDto Saved(PostSaveResult result)
    {
        return result is PostSaved saved
            ? saved.Post
            : throw new InvalidOperationException($"Expected the post to save, but got {result.GetType().Name}.");
    }
}
