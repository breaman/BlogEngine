using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the comment settings that change what happens to comments (design 8.3–8.5): closing comments N days after
/// publishing (C7, T4.17), auto-approving returning commenters (C8, T4.20), and emailing the author about pending
/// comments (C9, T4.21).
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.SiteSettings, TestConstraints.Comments, TestConstraints.Users])]
public class CommentSettingsTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Four links, two over the default limit: a spam score of 60.</summary>
    private const string SpamBody = "Deals at https://a.example https://b.example https://c.example https://d.example";

    /// <summary>Publishing sets the closing date from the setting, counted from the publish date; 0 days means never.</summary>
    [Test]
    public async Task Publish_SetsCloseDateFromSetting()
    {
        var publishOn = PublicTestPosts.Noon(PublicTestPosts.NextYear(), 3, 10);

        PostEditDto closing;
        await using (await SettingsTestData.ChangeAsync(factory, s => s.CloseCommentsAfterDays = 14))
        {
            closing = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Closes {PublicTestPosts.Token()}" }, publishOn);
        }

        var open = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Stays open {PublicTestPosts.Token()}" }, publishOn);

        await Assert.That(closing.CommentsCloseOn).IsEqualTo(publishOn.AddDays(14));
        await Assert.That(open.CommentsCloseOn).IsNull();
    }

    /// <summary>
    /// Once the closing date has passed, the post still shows its comments with "Comments are closed." instead of the
    /// form, and a submitted comment isn't stored (T4.17's done-when).
    /// </summary>
    [Test]
    public async Task ClosedByDate_ShowsCommentsAndRejectsNewOnes()
    {
        var marker = PublicTestPosts.Token();
        PostEditDto post;
        await using (await SettingsTestData.ChangeAsync(factory, s => s.CloseCommentsAfterDays = 7))
        {
            post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Old post {marker}", ContentMarkdown = "Body." },
                DateTimeOffset.UtcNow.AddDays(-30));
        }

        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, $"From before closing {marker}");
        using var client = CommentTestData.CreateClient(factory);

        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        // The closed page has no form, so post one as a bot would, with a token from a page that has one.
        var open = await CommentTestData.PublishPostAsync(factory, $"Open post {marker}");
        using var response = await CommentTestData.SubmitAsync(factory, client, post, $"Too late {marker}", tokenPage: open.PublicPath);

        await Assert.That(page).Contains($"From before closing {marker}");
        await Assert.That(page).Contains("Comments are closed.");
        await Assert.That(page).DoesNotContain("data-comment-form");
        await Assert.That(page).DoesNotContain("comment-reply-link");
        await Assert.That((await CommentTestData.ForPostAsync(factory, post.Id)).Select(c => c.BodyMarkdown)).DoesNotContain($"Too late {marker}");
    }

    /// <summary>With auto-approve on, a commenter with an approved comment is published at once; a new commenter waits.</summary>
    [Test]
    public async Task AutoApprove_On_PublishesReturningCommenter()
    {
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Auto-approve on {marker}");
        var returning = $"returning-{marker}@example.com";
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "An earlier, approved comment.", email: returning);
        using var client = CommentTestData.CreateClient(factory);

        await using (await SettingsTestData.ChangeAsync(factory, s => s.AutoApproveReturningCommenters = true))
        {
            using var again = await CommentTestData.SubmitAsync(factory, client, post, $"Back again {marker}.", email: returning.ToUpperInvariant());
            using var newcomer = await CommentTestData.SubmitAsync(factory, client, post, $"First time here {marker}.");

            await Assert.That(await CommentTestData.ReadHtmlAsync(again)).Contains("Your comment has been published");
            await Assert.That(await CommentTestData.ReadHtmlAsync(newcomer)).Contains("Your comment is awaiting moderation.");
        }

        var stored = await CommentTestData.ForPostAsync(factory, post.Id);
        using var reader = CommentTestData.CreateClient(factory);
        var page = await PublicTestPosts.GetOkAsync(reader, post.PublicPath!);

        await Assert.That(stored.Single(c => c.BodyMarkdown == $"Back again {marker}.").Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That(stored.Single(c => c.BodyMarkdown == $"First time here {marker}.").Status).IsEqualTo(CommentStatus.Pending);
        await Assert.That(page).Contains($"Back again {marker}.");
    }

    /// <summary>With auto-approve off (the default), even a returning commenter waits for moderation.</summary>
    [Test]
    public async Task AutoApprove_Off_HoldsReturningCommenter()
    {
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Auto-approve off {marker}");
        var returning = $"returning-{marker}@example.com";
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "An earlier, approved comment.", email: returning);
        using var client = CommentTestData.CreateClient(factory);

        await using (await SettingsTestData.ChangeAsync(factory, s => s.AutoApproveReturningCommenters = false))
        {
            using var again = await CommentTestData.SubmitAsync(factory, client, post, $"Back again {marker}.", email: returning);

            await Assert.That(await CommentTestData.ReadHtmlAsync(again)).Contains("Your comment is awaiting moderation.");
        }

        var stored = await CommentTestData.ForPostAsync(factory, post.Id);
        await Assert.That(stored.Single(c => c.BodyMarkdown == $"Back again {marker}.").Status).IsEqualTo(CommentStatus.Pending);
    }

    /// <summary>Auto-approve only skips moderation for a clean comment: anything the spam checks score still waits.</summary>
    [Test]
    public async Task AutoApprove_On_StillHoldsScoredComment()
    {
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Auto-approve scored {marker}");
        var returning = $"returning-{marker}@example.com";
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "An earlier, approved comment.", email: returning);
        using var client = CommentTestData.CreateClient(factory);

        await using (await SettingsTestData.ChangeAsync(factory, s => s.AutoApproveReturningCommenters = true))
        {
            using var linky = await CommentTestData.SubmitAsync(factory, client, post,
                $"See https://a.example https://b.example https://c.example {marker}", email: returning);
        }

        var stored = (await CommentTestData.ForPostAsync(factory, post.Id)).Single(c => c.BodyMarkdown.EndsWith(marker, StringComparison.Ordinal));
        await Assert.That(stored.Status).IsEqualTo(CommentStatus.Pending);
        await Assert.That(stored.SpamScore).IsGreaterThan(0);
    }

    /// <summary>
    /// With notifications on, a new pending comment emails the admin with the post and a link to the moderation queue
    /// (T4.21's done-when, with the test session's capturing transport standing in for Mailpit).
    /// </summary>
    [Test]
    public async Task PendingComment_EmailsAdmin_WhenNotificationsOn()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Notify {marker}");
        using var client = CommentTestData.CreateClient(factory);

        await using (await SettingsTestData.ChangeAsync(factory, s => s.NotifyOnPendingComment = true))
        {
            using var response = await CommentTestData.SubmitAsync(factory, client, post, $"Please approve me {marker}", name: "Hopeful Reader");
        }

        var email = await factory.Email.WaitForAsync(m => m.Subject.Contains(marker, StringComparison.Ordinal));

        await Assert.That(email).IsNotNull();
        await Assert.That(email!.To).Contains(IdentityTestHelper.AdminEmail);
        await Assert.That(email.Subject).IsEqualTo($"New comment on \"Notify {marker}\" awaiting moderation");
        await Assert.That(email.HtmlBody).Contains($"Please approve me {marker}");
        await Assert.That(email.HtmlBody).Contains("<strong>Hopeful Reader</strong>");
        await Assert.That(email.HtmlBody).Contains("/admin/comments\"");
        await Assert.That(email.TextBody).Contains($"{post.PublicPath}#comment-");
    }

    /// <summary>No email with notifications off, and none for spam or for comments published straight away.</summary>
    [Test]
    public async Task NoEmail_WhenOff_OrForSpamAndApproved()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Quiet {marker}");
        var returning = $"returning-{marker}@example.com";
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Approved before.", email: returning);
        using var client = CommentTestData.CreateClient(factory);

        await using (await SettingsTestData.ChangeAsync(factory, s => s.NotifyOnPendingComment = false))
        {
            using var off = await CommentTestData.SubmitAsync(factory, client, post, $"Pending while off {marker}");
        }

        await using (await SettingsTestData.ChangeAsync(factory, s =>
        {
            s.NotifyOnPendingComment = true;
            s.AutoApproveReturningCommenters = true;
        }))
        {
            using var spam = await CommentTestData.SubmitAsync(factory, client, post, SpamBody);
            using var approved = await CommentTestData.SubmitAsync(factory, client, post, $"Approved at once {marker}", email: returning);
        }

        var stored = await CommentTestData.ForPostAsync(factory, post.Id);
        await Assert.That(stored.Single(c => c.BodyMarkdown == SpamBody).Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(stored.Single(c => c.BodyMarkdown == $"Approved at once {marker}").Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That(await factory.Email.WaitForAsync(m => m.Subject.Contains(marker, StringComparison.Ordinal), TimeSpan.FromSeconds(1))).IsNull();
    }

    /// <summary>A comment moderated before its notification goes out is skipped rather than emailed.</summary>
    [Test]
    public async Task Sender_SkipsCommentsAlreadyModerated()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
        var post = await CommentTestData.PublishPostAsync(factory, $"Already handled {PublicTestPosts.Token()}");
        var pending = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Still waiting");
        var approved = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Handled already");
        var sender = factory.Services.GetRequiredService<CommentNotificationSender>();

        var sentPending = await sender.SendAsync(new CommentNotification(pending.Id, null), CancellationToken.None);
        var sentApproved = await sender.SendAsync(new CommentNotification(approved.Id, null), CancellationToken.None);

        await Assert.That(sentPending).IsTrue();
        await Assert.That(sentApproved).IsFalse();
    }
}