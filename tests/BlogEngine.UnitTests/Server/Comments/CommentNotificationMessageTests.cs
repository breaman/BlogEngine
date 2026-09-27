using BlogEngine.Server.Services.Comments;

namespace BlogEngine.UnitTests.Server.Comments;

/// <summary>
/// Tests the "new comment awaiting moderation" email (design 8.4, C9, T4.21): what it says, its links, and that nothing a
/// commenter typed can inject HTML.
/// </summary>
public class CommentNotificationMessageTests
{
    private static readonly Uri SiteRoot = new("https://blog.example/");

    private static CommentNotificationContent Content(string name = "Ada", string title = "Hello world", int score = 0, string? reasons = null) =>
        new(42, 7, title, new DateOnly(2026, 9, 23), "hello-world", name, "<p>Nice <em>post</em>!</p>", "Nice *post*!", score, reasons);

    /// <summary>The email names the commenter and post, quotes the comment and links to the post and the queue.</summary>
    [Test]
    public async Task Create_DescribesCommentWithLinks()
    {
        var message = CommentNotificationMessage.Create(Content(), ["author@example.com"], SiteRoot);

        await Assert.That(message.To).IsEquivalentTo(["author@example.com"]);
        await Assert.That(message.Subject).IsEqualTo("New comment on \"Hello world\" awaiting moderation");
        await Assert.That(message.HtmlBody).Contains("<strong>Ada</strong> commented on <a href=\"https://blog.example/posts/2026/09/23/hello-world#comment-42\">Hello world</a>");
        await Assert.That(message.HtmlBody).Contains("<p>Nice <em>post</em>!</p>");
        await Assert.That(message.HtmlBody).Contains("href=\"https://blog.example/admin/comments\"");
        await Assert.That(message.TextBody).Contains("Nice *post*!");
        await Assert.That(message.TextBody).Contains("Moderate it at https://blog.example/admin/comments");
        await Assert.That(message.HtmlBody).DoesNotContain("Spam score");
    }

    /// <summary>The commenter's name and the post title are encoded, so they can't add markup to the email.</summary>
    [Test]
    public async Task Create_EncodesNameAndTitle()
    {
        var message = CommentNotificationMessage.Create(Content(name: "<script>x</script>", title: "Fish & <chips>"), ["a@example.com"], SiteRoot);

        await Assert.That(message.HtmlBody).Contains("<strong>&lt;script&gt;x&lt;/script&gt;</strong>");
        await Assert.That(message.HtmlBody).Contains(">Fish &amp; &lt;chips&gt;</a>");
        await Assert.That(message.HtmlBody).DoesNotContain("<script>");
    }

    /// <summary>A non-zero spam score is shown with its reasons; without a site root the links are left out.</summary>
    [Test]
    public async Task Create_ShowsSpamScore_AndWorksWithoutSiteRoot()
    {
        var message = CommentNotificationMessage.Create(Content(score: 30, reasons: "3 links (more than 2)"), ["a@example.com"], siteRoot: null);

        await Assert.That(message.HtmlBody).Contains("<p>Spam score: 30 (3 links (more than 2))</p>");
        await Assert.That(message.HtmlBody).Contains("commented on <em>Hello world</em>");
        await Assert.That(message.HtmlBody).DoesNotContain("href=");
        await Assert.That(message.TextBody).DoesNotContain("Moderate it at");
    }
}