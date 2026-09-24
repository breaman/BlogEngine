using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the approved comments on the post page (design 14.2, T3.6): only approved comments appear, oldest first, with
/// the author's website as a nofollow link and never the email; approving a comment shows it despite caching.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Comments)]
public class CommentDisplayTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Pending, spam and rejected comments never appear; approved ones do, oldest first, without emails.</summary>
    [Test]
    public async Task OnlyApprovedComments_AreShown()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Shown {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, $"First approved {token}", email: $"first-{token}@example.com",
            name: "Grace", website: "https://grace.example");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Pending {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, $"Spam {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Rejected, $"Rejected {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, $"Second approved {token}");
        using var client = CommentTestData.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains("2 comments");
        await Assert.That(html.IndexOf($"First approved {token}", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf($"Second approved {token}", StringComparison.Ordinal));
        await Assert.That(html).Contains("<a class=\"comment-author fw-semibold\" href=\"https://grace.example\" rel=\"nofollow ugc noopener\"");
        await Assert.That(html).DoesNotContain($"first-{token}@example.com");
        await Assert.That(html).DoesNotContain($"Pending {token}");
        await Assert.That(html).DoesNotContain($"Spam {token}");
        await Assert.That(html).DoesNotContain($"Rejected {token}");
    }

    /// <summary>Approving a pending comment makes it appear on the next request, even after the page was cached.</summary>
    [Test]
    public async Task ApprovingComment_ShowsIt()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Approve me {token}");
        var pending = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Waiting {token}");
        using var client = CommentTestData.CreateClient(factory);

        var before = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        await ModerateAsync(pending.Id, CommentModerationAction.Approve);
        var after = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        await ModerateAsync(pending.Id, CommentModerationAction.Spam);
        var hidden = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(before).DoesNotContain($"Waiting {token}");
        await Assert.That(after).Contains($"Waiting {token}");
        await Assert.That(after).Contains($"id=\"comment-{pending.Id}\"");
        await Assert.That(hidden).DoesNotContain($"Waiting {token}");
    }

    private async Task ModerateAsync(int id, CommentModerationAction action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommentModerationService>().ModerateAsync(id, action);
    }
}
