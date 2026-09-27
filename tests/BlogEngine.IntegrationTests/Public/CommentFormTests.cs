using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the public comment form on the post page (design 8, T3.3, T3.4, T3.9): a plain form post with no JavaScript
/// stores a pending comment that isn't shown back, the honeypot and time trap discard bots silently, a blocked keyword
/// sends a comment to spam, and closed posts show "Comments are closed.".
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.SiteSettings, TestConstraints.Comments])]
public class CommentFormTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The post page renders a static form: antiforgery token, honeypot, signed timestamp and every field.</summary>
    [Test]
    public async Task PostPage_RendersStaticForm()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Form page {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains("data-comment-form");
        await Assert.That(html).Contains("method=\"post\"");
        await Assert.That(html).Contains("name=\"__RequestVerificationToken\"");
        await Assert.That(html).Contains("name=\"website2\"");
        await Assert.That(html).Contains("name=\"ts\"");
        await Assert.That(html).Contains("name=\"Input.AuthorName\"");
        await Assert.That(html).Contains("name=\"Input.Body\"");
        await Assert.That(html).Contains("No comments yet.");
    }

    /// <summary>
    /// A comment posted as a plain form (as a browser with JavaScript disabled sends it) is stored as pending with its
    /// IP hash and user agent, the commenter is thanked, and the pending comment is not shown on the page.
    /// </summary>
    [Test]
    public async Task Submit_WithoutJavaScript_StoresPendingComment()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Commented {token}");
        using var client = CommentTestData.CreateClient(factory);

        using var response = await CommentTestData.SubmitAsync(factory, client, post, $"A thoughtful reply {token}.",
            email: $"Reader-{token}@Example.com", website: "https://reader.example");
        var html = await CommentTestData.ReadHtmlAsync(response);
        var stored = (await CommentTestData.ForPostAsync(factory, post.Id)).Single();
        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Your comment is awaiting moderation.");
        await Assert.That(html).DoesNotContain($"A thoughtful reply {token}.</p>");
        await Assert.That(stored.Status).IsEqualTo(CommentStatus.Pending);
        await Assert.That(stored.AuthorEmail).IsEqualTo($"reader-{token}@example.com");
        await Assert.That(stored.AuthorUrl).IsEqualTo("https://reader.example");
        await Assert.That(stored.BodyHtml.Trim()).IsEqualTo($"<p>A thoughtful reply {token}.</p>");
        await Assert.That(stored.IpHash).Matches("^[0-9a-f]{64}$");
        await Assert.That(stored.UserAgent).IsEqualTo("CommentTests/1.0");
        await Assert.That(stored.SpamScore).IsEqualTo(0);
        await Assert.That(page).DoesNotContain($"A thoughtful reply {token}.");
    }

    /// <summary>Invalid fields re-render the form with messages and store nothing.</summary>
    [Test]
    public async Task Submit_Invalid_ShowsMessages()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Invalid {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        using var response = await CommentTestData.SubmitAsync(factory, client, post, "", email: "nope", website: "javascript:alert(1)");
        var html = await CommentTestData.ReadHtmlAsync(response);

        await Assert.That(html).Contains("'Email' is not a valid email address.");
        await Assert.That(html).Contains("'Comment' must not be empty.");
        await Assert.That(html).Contains("'Website' must be a full web address");
        await Assert.That(await CommentTestData.ForPostAsync(factory, post.Id)).IsEmpty();
    }

    /// <summary>A filled-in honeypot gets the same "thanks" but nothing is stored.</summary>
    [Test]
    public async Task Submit_Honeypot_IsDiscardedSilently()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Honeypot {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        using var response = await CommentTestData.SubmitAsync(factory, client, post, "Buy things", honeypot: "https://spam.example");

        await Assert.That(await CommentTestData.ReadHtmlAsync(response)).Contains("Your comment is awaiting moderation.");
        await Assert.That(await CommentTestData.ForPostAsync(factory, post.Id)).IsEmpty();
    }

    /// <summary>A form posted within 3 seconds of rendering, or with a forged timestamp, is discarded silently.</summary>
    [Test]
    public async Task Submit_TimeTrap_IsDiscardedSilently()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Too fast {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        using var tooFast = await CommentTestData.SubmitAsync(factory, client, post, "Hello quickly", formAge: TimeSpan.Zero);
        using var tooOld = await CommentTestData.SubmitAsync(factory, client, post, "Hello slowly", formAge: TimeSpan.FromHours(25));

        await Assert.That(await CommentTestData.ReadHtmlAsync(tooFast)).Contains("Your comment is awaiting moderation.");
        await Assert.That(await CommentTestData.ReadHtmlAsync(tooOld)).Contains("Your comment is awaiting moderation.");
        await Assert.That(await CommentTestData.ForPostAsync(factory, post.Id)).IsEmpty();
    }

    /// <summary>A keyword on the blocklist pushes a comment to spam (T3.9), with the reason recorded.</summary>
    [Test]
    public async Task Submit_BlockedKeyword_GoesToSpam()
    {
        var keyword = $"zorp{PublicTestPosts.Token()}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICommentModerationService>()
                .AddBlockAsync(new CommentBlockRequest { Kind = CommentBlockKind.Keyword, Value = keyword.ToUpperInvariant() });
        }

        var post = await CommentTestData.PublishPostAsync(factory, $"Keyword {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        using var response = await CommentTestData.SubmitAsync(factory, client, post, $"Totally normal text about {keyword} deals.");
        var stored = (await CommentTestData.ForPostAsync(factory, post.Id)).Single();

        await Assert.That(await CommentTestData.ReadHtmlAsync(response)).Contains("Your comment is awaiting moderation.");
        await Assert.That(stored.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(stored.SpamScore).IsEqualTo(50);
        await Assert.That(stored.SpamReasons).IsEqualTo($"Blocked keyword: {keyword}");
    }

    /// <summary>A post with comments turned off shows its approved comments and "Comments are closed." instead of the form.</summary>
    [Test]
    public async Task ClosedPost_ShowsNoticeInsteadOfForm()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Closed {token}", allowComments: false);
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, $"Earlier comment {token}");
        using var client = CommentTestData.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains("Comments are closed.");
        await Assert.That(html).Contains($"Earlier comment {token}");
        await Assert.That(html).DoesNotContain("data-comment-form");
    }

    /// <summary>Turning comments off site-wide hides the form everywhere (design 8.5).</summary>
    [Test]
    public async Task CommentsDisabled_HidesForm()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Switched off {PublicTestPosts.Token()}");
        using var client = CommentTestData.CreateClient(factory);

        await SetCommentsEnabledAsync(false);
        try
        {
            var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

            await Assert.That(html).DoesNotContain("data-comment-form");
            await Assert.That(html).DoesNotContain("id=\"comments\"");
        }
        finally
        {
            await SetCommentsEnabledAsync(true);
        }
    }

    private async Task SetCommentsEnabledAsync(bool enabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var current = await settings.GetAsync();
        current.CommentsEnabled = enabled;
        await settings.SaveAsync(current);
    }
}