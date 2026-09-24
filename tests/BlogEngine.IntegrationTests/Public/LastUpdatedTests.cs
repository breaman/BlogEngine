using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the "Last updated" date (P15, T4.8): changing the title or content of a live post stamps
/// <c>LastUpdatedOn</c>, which the post page shows as "Updated …" and the SEO head reports as the modified time; edits
/// before a post goes live, and changes to other fields, don't.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class LastUpdatedTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Editing a published post's content shows the updated date on the post page.</summary>
    [Test]
    public async Task EditingPublishedContent_ShowsUpdatedDate()
    {
        var post = await PublishOldPostAsync();
        using var client = IdentityTestHelper.CreateClient(factory);
        var before = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        post.ContentMarkdown += "\n\nA correction.";
        var updated = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(post.Id, post));
        var after = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(before).DoesNotContain("post-updated");
        await Assert.That(updated.LastUpdatedOn).IsNotNull();
        await Assert.That(after).Contains("<span class=\"post-updated\">Updated <time datetime=\"");
        await Assert.That(after).Contains($"<meta property=\"article:modified_time\" content=\"{updated.LastUpdatedOn!.Value.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}\"");
    }

    /// <summary>Changing only a published post's tags or summary isn't an update of its content.</summary>
    [Test]
    public async Task EditingOtherFields_DoesNotStampUpdate()
    {
        var post = await PublishOldPostAsync();

        post.Tags = [$"Later-{PublicTestPosts.Token()}"];
        post.Summary = "A new summary.";
        var saved = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(post.Id, post));

        await Assert.That(saved.LastUpdatedOn).IsNull();
    }

    /// <summary>Drafts and scheduled posts are still being written, so their edits aren't updates.</summary>
    [Test]
    public async Task EditingBeforePublishing_DoesNotStampUpdate()
    {
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {PublicTestPosts.Token()}", ContentMarkdown = "One." });
        draft.ContentMarkdown = "Two.";
        var savedDraft = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(draft.Id, draft));

        var scheduled = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Scheduled {PublicTestPosts.Token()}", ContentMarkdown = "One." },
            DateTimeOffset.UtcNow.AddDays(3));
        scheduled.ContentMarkdown = "Two.";
        var savedScheduled = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(scheduled.Id, scheduled));

        await Assert.That(savedDraft.LastUpdatedOn).IsNull();
        await Assert.That(savedScheduled.LastUpdatedOn).IsNull();
    }

    /// <summary>A post unpublished, edited as a draft and republished under its original date counts as updated.</summary>
    [Test]
    public async Task RepublishingEditedPost_StampsUpdate()
    {
        var post = await PublishOldPostAsync();
        var draft = await PublicTestPosts.SaveAsync(factory, s => s.UnpublishAsync(post.Id, new UnpublishPostRequest()));
        draft.ContentMarkdown = "Rewritten while unpublished.";
        draft = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(draft.Id, draft));

        var republished = await PublicTestPosts.SaveAsync(factory, s => s.PublishAsync(draft.Id, new PublishPostRequest()));

        await Assert.That(draft.LastUpdatedOn).IsNull();
        await Assert.That(republished.PublishedOn).IsEqualTo(post.PublishedOn);
        await Assert.That(republished.LastUpdatedOn).IsNotNull();
    }

    /// <summary>A post published at noon on a past day of its own test year, so an update today falls on a later date.</summary>
    private Task<PostEditDto> PublishOldPostAsync()
    {
        return PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Updated {PublicTestPosts.Token()}", ContentMarkdown = "Original." },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 6, 15));
    }
}
