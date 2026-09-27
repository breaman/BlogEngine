using System.Net;
using System.Net.Http.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Security;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the moderation API over HTTP (design 7.4, 8.4, T3.7, T3.9, T3.10, T4.15): authorization, each action, bulk
/// actions, blocking a commenter, emptying spam, the blocklist, the dashboard counts and author replies.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.Users, TestConstraints.Comments])]
public class AdminCommentsApiTests(BlogEngineWebApplicationFactory factory)
{
    private const string CommentsApi = "/api/admin/comments";
    private const string BlocksApi = "/api/admin/comment-blocks";

    /// <summary>Every comment endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("GET", CommentsApi);
        yield return ("GET", $"{CommentsApi}/counts");
        yield return ("POST", $"{CommentsApi}/1/approve");
        yield return ("POST", $"{CommentsApi}/1/reject");
        yield return ("POST", $"{CommentsApi}/1/spam");
        yield return ("DELETE", $"{CommentsApi}/1");
        yield return ("POST", $"{CommentsApi}/1/reply");
        yield return ("POST", $"{CommentsApi}/bulk");
        yield return ("POST", $"{CommentsApi}/1/block");
        yield return ("POST", $"{CommentsApi}/empty-spam");
        yield return ("GET", BlocksApi);
        yield return ("POST", BlocksApi);
        yield return ("DELETE", $"{BlocksApi}/1");
        yield return ("GET", "/api/admin/dashboard");
    }

    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>Anonymous callers get 401.</summary>
    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task Endpoint_Anonymous_Returns401(string method, string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.SendAsync(CreateRequest(method, path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signed-in users without the Admin role get 403.</summary>
    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task Endpoint_NonAdmin_Returns403(string method, string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.ReaderEmail);

        using var response = await client.SendAsync(CreateRequest(method, path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>Moderation actions need the antiforgery token.</summary>
    [Test]
    public async Task Approve_WithoutToken_IsRejected()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"No token {PublicTestPosts.Token()}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Hi");
        var (client, _) = await LoginAdminAsync();
        using var _ = client;

        using var response = await client.PostAsync($"{CommentsApi}/{comment.Id}/approve", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await CommentTestData.FindAsync(factory, comment.Id))!.Status).IsEqualTo(CommentStatus.Pending);
    }

    /// <summary>Each tab lists only its status, newest first, with the post and spam details.</summary>
    [Test]
    public async Task List_FiltersByStatus()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Listed {token}");
        var older = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, $"Old spam {token}", createdOn: DateTimeOffset.UtcNow.AddDays(-1));
        var newer = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, $"New spam {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Pending {token}");
        var (client, _) = await LoginAdminAsync();
        using var _ = client;

        var spam = await client.GetFromJsonAsync<PagedResult<CommentDto>>($"{CommentsApi}?status=spam&pageSize=100");
        using var badStatus = await client.GetAsync($"{CommentsApi}?status=hidden");

        var ours = spam!.Items.Where(c => c.PostId == post.Id).ToList();
        await Assert.That(ours.Select(c => c.Id)).IsEquivalentTo([newer.Id, older.Id]);
        await Assert.That(ours[0].PostTitle).IsEqualTo($"Listed {token}");
        await Assert.That(ours[0].PostPath).IsEqualTo(post.PublicPath);
        await Assert.That(ours[0].AuthorEmail).IsEqualTo(newer.AuthorEmail);
        await Assert.That(badStatus.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>Approve, reject and spam change the status and record who moderated; unknown ids are 404.</summary>
    [Test]
    [Arguments("approve", CommentStatus.Approved)]
    [Arguments("reject", CommentStatus.Rejected)]
    [Arguments("spam", CommentStatus.Spam)]
    public async Task Action_ChangesStatus(string action, CommentStatus expected)
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Action {PublicTestPosts.Token()}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Please moderate me");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{comment.Id}/{action}");
        using var missing = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{int.MaxValue}/{action}");
        var stored = await CommentTestData.FindAsync(factory, comment.Id);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(stored!.Status).IsEqualTo(expected);
        await Assert.That(stored.ModeratedOn).IsNotNull();
        await Assert.That(stored.ModeratedBy ?? 0).IsGreaterThan(0);
    }

    /// <summary>Delete removes the comment permanently, with its replies.</summary>
    [Test]
    public async Task Delete_RemovesCommentAndReplies()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Delete {PublicTestPosts.Token()}");
        var parent = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Parent");
        var reply = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Reply", parentId: parent.Id);
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Delete, $"{CommentsApi}/{parent.Id}");
        using var again = await SendAsync(client, token, HttpMethod.Delete, $"{CommentsApi}/{parent.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await CommentTestData.FindAsync(factory, parent.Id)).IsNull();
        await Assert.That(await CommentTestData.FindAsync(factory, reply.Id)).IsNull();
    }

    /// <summary>A bulk action applies to every selected comment; an empty selection is a 400.</summary>
    [Test]
    public async Task Bulk_AppliesToEachComment()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Bulk {PublicTestPosts.Token()}");
        var first = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "One");
        var second = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, "Two");
        var untouched = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Three");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/bulk",
            new CommentBulkRequest { Ids = [first.Id, second.Id, int.MaxValue], Action = CommentModerationAction.Approve });
        var result = await response.Content.ReadFromJsonAsync<BulkModerationResponse>();
        using var empty = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/bulk",
            new CommentBulkRequest { Ids = [], Action = CommentModerationAction.Approve });
        using var deleted = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/bulk",
            new CommentBulkRequest { Ids = [first.Id], Action = CommentModerationAction.Delete });

        await Assert.That(result!.Changed).IsEqualTo(2);
        await Assert.That((await CommentTestData.FindAsync(factory, second.Id))!.Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That((await CommentTestData.FindAsync(factory, untouched.Id))!.Status).IsEqualTo(CommentStatus.Pending);
        await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await CommentTestData.FindAsync(factory, first.Id)).IsNull();
    }

    /// <summary>
    /// "Block commenter" adds email and IP blocks, marks all of that commenter's comments (by email or IP) as spam, and
    /// their next comment goes straight to spam.
    /// </summary>
    [Test]
    public async Task BlockCommenter_BlocksAndMarksSpam()
    {
        var tokenText = PublicTestPosts.Token();
        var email = $"troll-{tokenText}@example.com";
        var ipHash = new string('a', 32) + tokenText.PadRight(32, '0');
        var post = await CommentTestData.PublishPostAsync(factory, $"Block {tokenText}");
        var target = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Troll 1", email: email, ipHash: ipHash);
        var sameEmail = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Troll 2", email: email);
        var sameIp = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Troll 3", ipHash: ipHash);
        var bystander = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Nice person");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{target.Id}/block");
        var result = await response.Content.ReadFromJsonAsync<CommenterBlocked>();
        var blocks = await client.GetFromJsonAsync<List<CommentBlockDto>>(BlocksApi);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(result!.CommentsMarkedSpam).IsEqualTo(3);
        await Assert.That(result.Blocks.Select(b => (b.Kind, b.Value))).IsEquivalentTo(
            [(CommentBlockKind.Email, email), (CommentBlockKind.IpHash, ipHash)]);
        await Assert.That(blocks!.Select(b => b.Value)).Contains(email);
        foreach (var id in new[] { target.Id, sameEmail.Id, sameIp.Id })
        {
            await Assert.That((await CommentTestData.FindAsync(factory, id))!.Status).IsEqualTo(CommentStatus.Spam);
        }

        await Assert.That((await CommentTestData.FindAsync(factory, bystander.Id))!.Status).IsEqualTo(CommentStatus.Approved);

        // The blocked email's next comment, through the real form, goes straight to spam.
        using var reader = CommentTestData.CreateClient(factory);
        using var _2 = await CommentTestData.SubmitAsync(factory, reader, post, "I'm back", email: email.ToUpperInvariant());
        var comeback = (await CommentTestData.ForPostAsync(factory, post.Id)).Last();
        await Assert.That(comeback.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(comeback.SpamReasons).Contains("Blocked email");
    }

    /// <summary>Empty spam deletes spam older than 30 days and keeps newer spam and everything else.</summary>
    [Test]
    public async Task EmptySpam_DeletesOnlyOldSpam()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Empty spam {PublicTestPosts.Token()}");
        var old = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, "Old spam", createdOn: DateTimeOffset.UtcNow.AddDays(-31));
        var recent = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, "Recent spam", createdOn: DateTimeOffset.UtcNow.AddDays(-29));
        var oldRejected = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Rejected, "Old rejected", createdOn: DateTimeOffset.UtcNow.AddDays(-60));
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/empty-spam");
        var result = await response.Content.ReadFromJsonAsync<EmptySpamResponse>();

        await Assert.That(result!.Deleted).IsGreaterThanOrEqualTo(1);
        await Assert.That(await CommentTestData.FindAsync(factory, old.Id)).IsNull();
        await Assert.That(await CommentTestData.FindAsync(factory, recent.Id)).IsNotNull();
        await Assert.That(await CommentTestData.FindAsync(factory, oldRejected.Id)).IsNotNull();
    }

    /// <summary>Keyword and domain blocks can be added (normalized, without duplicates), validated, and removed.</summary>
    [Test]
    public async Task Blocklist_AddValidateAndRemove()
    {
        var domain = $"spam-{PublicTestPosts.Token()}.example";
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var added = await SendAsync(client, token, HttpMethod.Post, BlocksApi,
            new CommentBlockRequest { Kind = CommentBlockKind.Domain, Value = $" @{domain.ToUpperInvariant()} ", Note = "Link farm" });
        var block = await added.Content.ReadFromJsonAsync<CommentBlockDto>();
        using var duplicate = await SendAsync(client, token, HttpMethod.Post, BlocksApi,
            new CommentBlockRequest { Kind = CommentBlockKind.Domain, Value = domain });
        var same = await duplicate.Content.ReadFromJsonAsync<CommentBlockDto>();
        using var invalid = await SendAsync(client, token, HttpMethod.Post, BlocksApi,
            new CommentBlockRequest { Kind = CommentBlockKind.Domain, Value = "not a domain" });
        using var removed = await SendAsync(client, token, HttpMethod.Delete, $"{BlocksApi}/{block!.Id}");
        using var removedAgain = await SendAsync(client, token, HttpMethod.Delete, $"{BlocksApi}/{block.Id}");
        var remaining = await client.GetFromJsonAsync<List<CommentBlockDto>>(BlocksApi);

        await Assert.That(block.Value).IsEqualTo(domain);
        await Assert.That(block.Note).IsEqualTo("Link farm");
        await Assert.That(same!.Id).IsEqualTo(block.Id);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(removedAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(remaining!.Select(b => b.Id)).DoesNotContain(block.Id);
    }

    /// <summary>The tab counts and the dashboard's pending count are accurate and follow moderation (T3.10).</summary>
    [Test]
    public async Task CountsAndDashboard_FollowModeration()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Counted {PublicTestPosts.Token()}");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var countsBefore = await client.GetFromJsonAsync<CommentStatusCounts>($"{CommentsApi}/counts");
        var dashboardBefore = await client.GetFromJsonAsync<DashboardSummaryDto>("/api/admin/dashboard");

        var first = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Count me");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Count me too");
        var countsAdded = await client.GetFromJsonAsync<CommentStatusCounts>($"{CommentsApi}/counts");
        using var approve = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{first.Id}/approve");
        var countsAfter = await client.GetFromJsonAsync<CommentStatusCounts>($"{CommentsApi}/counts");
        var dashboardAfter = await client.GetFromJsonAsync<DashboardSummaryDto>("/api/admin/dashboard");

        await Assert.That(countsAdded!.Pending).IsEqualTo(countsBefore!.Pending + 2);
        await Assert.That(countsAfter!.Pending).IsEqualTo(countsBefore.Pending + 1);
        await Assert.That(countsAfter.Approved).IsEqualTo(countsBefore.Approved + 1);
        await Assert.That(dashboardBefore!.PendingCommentCount).IsEqualTo(countsBefore.Pending);
        await Assert.That(dashboardAfter!.PendingCommentCount).IsEqualTo(countsBefore.Pending + 1);
        await Assert.That(dashboardAfter.PublishedCount).IsGreaterThanOrEqualTo(1);
        await Assert.That(dashboardAfter.RecentComments.Select(c => c.BodyHtml)).Contains("<p>Count me too</p>");
        await Assert.That(dashboardAfter.RecentPosts).IsNotEmpty();
    }

    /// <summary>
    /// Replying from the queue publishes an author reply under the comment, approves the pending comment, and both show on
    /// the post page with the reply's "Author" badge (T4.15's done-when).
    /// </summary>
    [Test]
    public async Task Reply_PublishesAuthorReply_AndApprovesParent()
    {
        var marker = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Replied {marker}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Question {marker}", name: "Curious Reader");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{comment.Id}/reply",
            new CommentReplyRequest { Body = $"**Answer** {marker}" });
        var replied = await response.Content.ReadFromJsonAsync<CommentReplied>();
        var stored = await CommentTestData.ForPostAsync(factory, post.Id);
        using var reader = CommentTestData.CreateClient(factory);
        var page = await PublicTestPosts.GetOkAsync(reader, post.PublicPath!);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(replied!.ApprovedCommentIds).IsEquivalentTo([comment.Id]);
        await Assert.That(replied.Reply.IsAuthorReply).IsTrue();
        await Assert.That(replied.Reply.ParentCommentId).IsEqualTo(comment.Id);
        await Assert.That(replied.Reply.Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That(replied.Reply.BodyHtml).Contains($"<strong>Answer</strong> {marker}");
        await Assert.That(stored.Single(c => c.Id == comment.Id).Status).IsEqualTo(CommentStatus.Approved);
        await Assert.That(stored.Single(c => c.IsAuthorReply).AuthorEmail).IsEqualTo(IdentityTestHelper.AdminEmail.ToLowerInvariant());

        // The reply sits in the parent's thread, with the badge.
        var thread = page[page.IndexOf($"id=\"comment-{comment.Id}\"", StringComparison.Ordinal)..];
        await Assert.That(page).Contains($"Question {marker}");
        await Assert.That(thread).Contains("comment-replies");
        await Assert.That(thread).Contains($"id=\"comment-{replied.Reply.Id}\"");
        await Assert.That(thread[thread.IndexOf($"id=\"comment-{replied.Reply.Id}\"", StringComparison.Ordinal)..]).Contains(">Author</span>");
    }

    /// <summary>Answering a reply files the answer under the top-level comment, so threads stay one level deep (T4.16).</summary>
    [Test]
    public async Task Reply_ToReply_AttachesToTopLevelComment()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Deep thread {PublicTestPosts.Token()}");
        var top = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Top");
        var reply = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Reply", parentId: top.Id);
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{reply.Id}/reply", new CommentReplyRequest { Body = "Answer" });
        var replied = await response.Content.ReadFromJsonAsync<CommentReplied>();

        await Assert.That(replied!.Reply.ParentCommentId).IsEqualTo(top.Id);
        await Assert.That(replied.ApprovedCommentIds).IsEquivalentTo([reply.Id]);
        await Assert.That((await CommentTestData.FindAsync(factory, reply.Id))!.Status).IsEqualTo(CommentStatus.Approved);
    }

    /// <summary>An empty reply is a validation problem, an unknown comment a 404, and a reply needs the antiforgery token.</summary>
    [Test]
    public async Task Reply_InvalidRequests()
    {
        var post = await CommentTestData.PublishPostAsync(factory, $"Bad reply {PublicTestPosts.Token()}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Hello");
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var empty = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/{comment.Id}/reply", new CommentReplyRequest { Body = " " });
        using var missing = await SendAsync(client, token, HttpMethod.Post, $"{CommentsApi}/999999999/reply", new CommentReplyRequest { Body = "Hi" });
        using var noToken = await client.PostAsJsonAsync($"{CommentsApi}/{comment.Id}/reply", new CommentReplyRequest { Body = "Hi" });

        await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(noToken.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CommentTestData.ForPostAsync(factory, post.Id)).HasSingleItem();
        await Assert.That((await CommentTestData.FindAsync(factory, comment.Id))!.Status).IsEqualTo(CommentStatus.Pending);
    }

    private async Task<(HttpClient Client, string Token)> LoginAdminAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return (client, await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin"));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Add(AntiforgeryHeaders.RequestToken, token);
        return client.SendAsync(request);
    }

    /// <summary>A request with a minimal body for writes, so it reaches authorization rather than failing binding.</summary>
    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST")
        {
            request.Content = JsonContent.Create(new { ids = new[] { 1 }, kind = 2, value = "x" });
        }

        return request;
    }
}