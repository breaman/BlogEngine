using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Enums;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the comment rate limit (design 8.3, T3.5): three comments per five minutes per IP address, then a friendly 429.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.SiteSettings, TestConstraints.Comments])]
public class CommentRateLimitTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The fourth comment from one address within the window is rejected; another address is unaffected.</summary>
    [Test]
    public async Task FourthCommentInWindow_IsRejected()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Rate limited {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);
        using var other = CommentTestData.CreateClient(factory);

        for (var i = 1; i <= 3; i++)
        {
            using var accepted = await CommentTestData.SubmitAsync(factory, client, post, $"Comment number {i}");
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using var rejected = await CommentTestData.SubmitAsync(factory, client, post, "Comment number 4");
        var html = await CommentTestData.ReadHtmlAsync(rejected);
        using var fromElsewhere = await CommentTestData.SubmitAsync(factory, other, post, "Someone else");
        var stored = await CommentTestData.ForPostAsync(factory, post.Id);

        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(html).Contains("Please wait");
        await Assert.That(rejected.Headers.RetryAfter).IsNotNull();
        await Assert.That(fromElsewhere.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(stored.Select(c => c.BodyMarkdown)).DoesNotContain("Comment number 4");
        await Assert.That(stored.Count(c => c.Status == CommentStatus.Pending)).IsEqualTo(4);
    }

    /// <summary>Reading the post page is never rate limited.</summary>
    [Test]
    public async Task Reading_IsNotLimited()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Read often {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        for (var i = 0; i < 6; i++)
        {
            await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        }
    }
}
