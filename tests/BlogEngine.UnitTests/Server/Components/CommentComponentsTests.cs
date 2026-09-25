using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services.Public;

using Bunit;

namespace BlogEngine.UnitTests.Server.Components;

/// <summary>
/// Component tests for the post page's comment list (design 14.2, T3.6): <see cref="CommentList"/> threading,
/// <see cref="CommentItem"/> rendering and the <see cref="CommentAvatar"/> helpers.
/// </summary>
public class CommentComponentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = PublicCommentQueries.AvatarHash("ada@example.com");

    private static PublicComment Comment(int id, int? parent = null, string name = "Ada", string? url = null, bool author = false) =>
        new(id, parent, name, url, $"<p>Comment {id}</p>", Now.AddHours(-id), author, Hash);

    /// <summary>Replies sit under their parent in order; a reply whose parent isn't shown stands on its own.</summary>
    [Test]
    public async Task Thread_GroupsRepliesUnderParents()
    {
        var threads = CommentList.Thread([Comment(1), Comment(2, parent: 1), Comment(3), Comment(4, parent: 1), Comment(5, parent: 99)]);

        await Assert.That(threads.Select(t => t.Comment.Id)).IsEquivalentTo([1, 3, 5]);
        await Assert.That(threads[0].Replies.Select(r => r.Id)).IsEquivalentTo([2, 4]);
        await Assert.That(threads[1].Replies).IsEmpty();
    }

    /// <summary>
    /// A comment shows the name linked to the website with nofollow, an anchor, a relative date and the body; the email
    /// and its hash never reach the page when avatars are off.
    /// </summary>
    [Test]
    public async Task Item_RendersAuthorDateAndBody()
    {
        await using var context = new BunitContext();

        var cut = context.Render<CommentItem>(p => p
            .Add(x => x.Comment, Comment(2, url: "https://ada.example"))
            .Add(x => x.Now, Now));

        var link = cut.Find("a.comment-author");
        await Assert.That(link.GetAttribute("href")).IsEqualTo("https://ada.example");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("nofollow ugc noopener");
        await Assert.That(cut.Find("article").Id).IsEqualTo("comment-2");
        await Assert.That(cut.Find("time").TextContent).IsEqualTo("2 hours ago");
        await Assert.That(cut.Markup).Contains("<p>Comment 2</p>");
        await Assert.That(cut.Markup).DoesNotContain(Hash);
        await Assert.That(cut.Markup).DoesNotContain("gravatar");
        await Assert.That(cut.Find(".comment-avatar").TextContent).IsEqualTo("A");
    }

    /// <summary>With avatars on, a Gravatar identicon is shown instead of the initial.</summary>
    [Test]
    public async Task Item_WithAvatars_ShowsGravatar()
    {
        await using var context = new BunitContext();

        var cut = context.Render<CommentItem>(p => p.Add(x => x.Comment, Comment(1)).Add(x => x.Now, Now).Add(x => x.ShowAvatars, true));

        await Assert.That(cut.Find("img.comment-avatar").GetAttribute("src"))
            .IsEqualTo($"https://www.gravatar.com/avatar/{Hash}?s=80&d=identicon");
    }

    /// <summary>The author's own replies carry an "Author" badge.</summary>
    [Test]
    public async Task Item_AuthorReply_HasBadge()
    {
        await using var context = new BunitContext();

        var cut = context.Render<CommentItem>(p => p.Add(x => x.Comment, Comment(1, author: true)).Add(x => x.Now, Now));

        await Assert.That(cut.Find(".badge").TextContent).IsEqualTo("Author");
    }

    /// <summary>The list nests replies one level deep.</summary>
    [Test]
    public async Task List_NestsReplies()
    {
        await using var context = new BunitContext();

        var cut = context.Render<CommentList>(p => p.Add(x => x.Comments, [Comment(1), Comment(2, parent: 1)]).Add(x => x.Now, Now));

        await Assert.That(cut.FindAll(".comment-replies #comment-2")).Count().IsEqualTo(1);
        await Assert.That(cut.FindAll(".comment-replies #comment-1")).IsEmpty();
    }

    /// <summary>The initial is the first letter or digit, upper-cased, with a fallback.</summary>
    [Test]
    [Arguments("ada", "A")]
    [Arguments("  émile", "É")]
    [Arguments("@@42", "4")]
    [Arguments("", "?")]
    [Arguments(null, "?")]
    public async Task Avatar_Initial(string? name, string expected)
    {
        await Assert.That(CommentAvatar.Initial(name)).IsEqualTo(expected);
    }

    /// <summary>The color is stable for a hash and always a known Bootstrap class.</summary>
    [Test]
    public async Task Avatar_ColorIsStable()
    {
        await Assert.That(CommentAvatar.ColorClass(Hash)).IsEqualTo(CommentAvatar.ColorClass(Hash));
        await Assert.That(CommentAvatar.ColorClass(Hash)).StartsWith("text-bg-");
        await Assert.That(CommentAvatar.ColorClass(null)).StartsWith("text-bg-");
    }

    /// <summary>
    /// While comments are open every comment, replies included, gets a "Reply" link to <c>?replyTo=</c> and the form
    /// (T4.16); without a reply path there are none.
    /// </summary>
    [Test]
    public async Task List_ReplyLinks_OnlyWhileOpen()
    {
        await using var context = new BunitContext();
        IReadOnlyList<PublicComment> comments = [Comment(1), Comment(2, parent: 1)];

        var open = context.Render<CommentList>(p => p
            .Add(x => x.Comments, comments)
            .Add(x => x.Now, Now)
            .Add(x => x.ReplyPath, "/posts/2026/09/23/hello"));
        var closed = context.Render<CommentList>(p => p
            .Add(x => x.Comments, comments)
            .Add(x => x.Now, Now));

        var links = open.FindAll("a.comment-reply-link");
        await Assert.That(links.Select(l => l.GetAttribute("href") ?? string.Empty))
            .IsEquivalentTo(["/posts/2026/09/23/hello?replyTo=1#comment-form", "/posts/2026/09/23/hello?replyTo=2#comment-form"]);
        await Assert.That(links[0].GetAttribute("rel")).IsEqualTo("nofollow");
        await Assert.That(links[0].GetAttribute("aria-label")).IsEqualTo("Reply to Ada");
        await Assert.That(closed.FindAll("a.comment-reply-link")).IsEmpty();
    }
}
