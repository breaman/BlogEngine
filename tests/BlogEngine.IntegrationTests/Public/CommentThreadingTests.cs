using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Components.Blog;
using BlogEngine.Shared.Enums;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests replies on the post page (design 6.5, 8.4, C5, C6, T4.15, T4.16): "Reply" links, the reply form reached through
/// them without JavaScript, one level of nesting, and the signed-in author commenting from the post page.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.Users, TestConstraints.Comments])]
public class CommentThreadingTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Every approved comment gets a "Reply" link, and following it turns the form into a reply to that comment.</summary>
    [Test]
    public async Task ReplyLink_OpensFormAsReply()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Reply link {PublicTestPosts.Token()}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "First!", name: "Grace");
        using var client = CommentTestData.CreateClient(factory);

        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        var replyPage = await PublicTestPosts.GetOkAsync(client, $"{post.PublicPath}?{CommentList.ReplyToParameter}={comment.Id}");

        await Assert.That(page).Contains($"href=\"{post.PublicPath}?replyTo={comment.Id}#comment-form\"");
        await Assert.That(page).Contains("Leave a comment");
        await Assert.That(replyPage).Contains("Reply to Grace");
        await Assert.That(replyPage).Contains($"name=\"Input.ParentCommentId\" value=\"{comment.Id}\"");
        await Assert.That(replyPage).Contains($"Replying to <a href=\"{post.PublicPath}#comment-{comment.Id}\"");
    }

    /// <summary>A reply target that isn't an approved comment on the post is ignored: the form is for a new thread.</summary>
    [Test]
    public async Task ReplyLink_UnknownOrHiddenComment_ShowsPlainForm()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Hidden target {PublicTestPosts.Token()}");
        var pending = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Not yet approved");
        using var client = CommentTestData.CreateClient(factory);

        var hidden = await PublicTestPosts.GetOkAsync(client, $"{post.PublicPath}?replyTo={pending.Id}");
        var unknown = await PublicTestPosts.GetOkAsync(client, $"{post.PublicPath}?replyTo=999999999");

        foreach (var html in new[] { hidden, unknown })
        {
            await Assert.That(html).Contains("Leave a comment");
            await Assert.That(html).DoesNotContain("Input.ParentCommentId");
        }
    }

    /// <summary>
    /// A reply posted without JavaScript is stored under its parent; a reply to a reply goes under the top-level comment,
    /// so nesting never exceeds one level (T4.16's done-when).
    /// </summary>
    [Test]
    public async Task Submit_Reply_NestsOneLevelDeep()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Threaded {PublicTestPosts.Token()}");
        var top = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Top-level comment");
        var reply = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "A reply", parentId: top.Id);
        using var client = CommentTestData.CreateClient(factory);

        using var first = await CommentTestData.SubmitAsync(factory, client, post, "Replying to the top.", parentId: top.Id);
        using var second = await CommentTestData.SubmitAsync(factory, client, post, "Replying to the reply.", parentId: reply.Id);
        var stored = await CommentTestData.ForPostAsync(factory, post.Id);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await CommentTestData.ReadHtmlAsync(second)).Contains("Your comment is awaiting moderation.");
        await Assert.That(stored.Single(c => c.BodyMarkdown == "Replying to the top.").ParentCommentId).IsEqualTo(top.Id);
        await Assert.That(stored.Single(c => c.BodyMarkdown == "Replying to the reply.").ParentCommentId).IsEqualTo(top.Id);
        await Assert.That(stored.Where(c => c.ParentCommentId is not null).Select(c => c.ParentCommentId)).DoesNotContain(reply.Id);
    }

    /// <summary>A parent that readers can't see (pending, or on another post) makes the comment a new thread instead.</summary>
    [Test]
    public async Task Submit_ReplyToHiddenOrForeignComment_StartsNewThread()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Foreign parent {PublicTestPosts.Token()}");
        var other = await CommentTestData.PublishPostAsync(factory, $"Other post {PublicTestPosts.Token()}");
        var pending = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Pending parent");
        var elsewhere = await CommentTestData.AddAsync(factory, other.Id, CommentStatus.Approved, "Parent on another post");
        using var client = CommentTestData.CreateClient(factory);

        using var toPending = await CommentTestData.SubmitAsync(factory, client, post, "Reply to pending.", parentId: pending.Id);
        using var toOther = await CommentTestData.SubmitAsync(factory, client, post, "Reply across posts.", parentId: elsewhere.Id);
        var stored = await CommentTestData.ForPostAsync(factory, post.Id);

        await Assert.That(stored.Single(c => c.BodyMarkdown == "Reply to pending.").ParentCommentId).IsNull();
        await Assert.That(stored.Single(c => c.BodyMarkdown == "Reply across posts.").ParentCommentId).IsNull();
    }

    /// <summary>Replies are shown indented under their parent, and a reply's own "Reply" link targets the same thread.</summary>
    [Test]
    public async Task PostPage_ShowsRepliesIndentedUnderParent()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Indented {PublicTestPosts.Token()}");
        var top = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Parent comment");
        var reply = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Child comment", parentId: top.Id);
        using var client = CommentTestData.CreateClient(factory);

        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        var replies = page.IndexOf("comment-replies", StringComparison.Ordinal);
        await Assert.That(replies).IsGreaterThan(page.IndexOf($"id=\"comment-{top.Id}\"", StringComparison.Ordinal));
        await Assert.That(page.IndexOf($"id=\"comment-{reply.Id}\"", StringComparison.Ordinal)).IsGreaterThan(replies);
        await Assert.That(page).Contains($"?replyTo={reply.Id}#comment-form");
    }

    /// <summary>
    /// The signed-in author sees a form with only the comment field, and what they post is published at once as an
    /// author reply in the chosen thread (T4.15, "from the post").
    /// </summary>
    [Test]
    public async Task Author_CommentsFromPostPage()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Author on page {marker}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Reader question");
        using var client = CommentTestData.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);

        var form = await PublicTestPosts.GetOkAsync(client, $"{post.PublicPath}?replyTo={comment.Id}");
        using var response = await IdentityTestHelper.SubmitFormAsync(client, post.PublicPath!, CommentForm.FormName, new Dictionary<string, string>
        {
            ["Input.Body"] = $"Author answer {marker}",
            ["Input.ParentCommentId"] = comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var html = await CommentTestData.ReadHtmlAsync(response);
        var stored = (await CommentTestData.ForPostAsync(factory, post.Id)).Single(c => c.BodyMarkdown == $"Author answer {marker}");
        using var reader = CommentTestData.CreateClient(factory);
        var page = await PublicTestPosts.GetOkAsync(reader, post.PublicPath!);

        await Assert.That(form).Contains("signed in as the blog's author");
        await Assert.That(form).DoesNotContain("name=\"Input.AuthorEmail\"");
        await Assert.That(html).Contains("Your comment has been published");
        await Assert.That(stored.Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That(stored.IsAuthorReply).IsTrue();
        await Assert.That(stored.ParentCommentId).IsEqualTo(comment.Id);
        await Assert.That(page).Contains($"Author answer {marker}");
    }
}