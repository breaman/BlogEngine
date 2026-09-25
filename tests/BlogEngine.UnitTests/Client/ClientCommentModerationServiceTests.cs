using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

using FluentValidation;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientCommentModerationService"/> (T3.7, T3.8): requests go to the comment API routes, and status
/// codes come back as the same values the server implementation returns.
/// </summary>
public class ClientCommentModerationServiceTests
{
    /// <summary>The tab and page become query parameters.</summary>
    [Test]
    public async Task GetCommentsAsync_SendsStatusAndPage()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new PagedResult<CommentDto>([], 0, 2, 25)));

        await CreateService(handler).GetCommentsAsync(new CommentListQuery { Status = CommentStatus.Spam, Page = 2 });

        await Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery).IsEqualTo("/api/admin/comments?status=Spam&page=2&pageSize=25");
    }

    /// <summary>Each action goes to its own route with the right verb.</summary>
    [Test]
    [Arguments(CommentModerationAction.Approve, "POST", "/api/admin/comments/4/approve")]
    [Arguments(CommentModerationAction.Reject, "POST", "/api/admin/comments/4/reject")]
    [Arguments(CommentModerationAction.Spam, "POST", "/api/admin/comments/4/spam")]
    [Arguments(CommentModerationAction.Delete, "DELETE", "/api/admin/comments/4")]
    public async Task ModerateAsync_UsesActionRoute(CommentModerationAction action, string method, string path)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var found = await CreateService(handler).ModerateAsync(4, action);

        await Assert.That(found).IsTrue();
        await Assert.That(handler.Requests.Single().Method.Method).IsEqualTo(method);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo(path);
    }

    /// <summary>A 404 means the comment is gone.</summary>
    [Test]
    public async Task ModerateAsync_NotFound_ReturnsFalse()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.That(await CreateService(handler).ModerateAsync(4, CommentModerationAction.Approve)).IsFalse();
    }

    /// <summary>A rejected bulk request throws a validation exception with the server's messages.</summary>
    [Test]
    public async Task ModerateManyAsync_BadRequest_Throws()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest,
            new { title = "One or more validation errors occurred.", errors = new Dictionary<string, string[]> { ["Ids"] = ["Select at least one comment."] } }));

        var exception = await Assert.That(async () => await CreateService(handler).ModerateManyAsync(new CommentBulkRequest()))
            .Throws<ValidationException>();

        await Assert.That(exception!.Errors.Single().ErrorMessage).IsEqualTo("Select at least one comment.");
    }

    /// <summary>An invalid block comes back as <see cref="CommentBlockInvalid"/>; a saved one as <see cref="CommentBlockSaved"/>.</summary>
    [Test]
    public async Task AddBlockAsync_MapsResults()
    {
        var invalid = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest,
            new { errors = new Dictionary<string, string[]> { ["Value"] = ["Enter a domain, such as example.com."] } }));
        var saved = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new CommentBlockDto { Id = 3, Kind = CommentBlockKind.Domain, Value = "spam.example" }));

        var invalidResult = await CreateService(invalid).AddBlockAsync(new CommentBlockRequest { Kind = CommentBlockKind.Domain, Value = "x" });
        var savedResult = await CreateService(saved).AddBlockAsync(new CommentBlockRequest { Kind = CommentBlockKind.Domain, Value = "spam.example" });

        await Assert.That(((CommentBlockInvalid)invalidResult).Errors["Value"].Single()).Contains("domain");
        await Assert.That(((CommentBlockSaved)savedResult).Block.Id).IsEqualTo(3);
        await Assert.That(saved.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/comment-blocks");
    }

    /// <summary>Block commenter returns the result, or <see langword="null"/> for an unknown comment.</summary>
    [Test]
    public async Task BlockCommenterAsync_MapsResults()
    {
        var ok = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new CommenterBlocked([], 3)));
        var missing = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.That((await CreateService(ok).BlockCommenterAsync(9))!.CommentsMarkedSpam).IsEqualTo(3);
        await Assert.That(ok.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/comments/9/block");
        await Assert.That(await CreateService(missing).BlockCommenterAsync(9)).IsNull();
    }

    /// <summary>A reply is posted to the comment's reply route and comes back with the comments it approved (T4.15).</summary>
    [Test]
    public async Task ReplyAsync_PostsAndReadsReply()
    {
        var reply = new CommentDto { Id = 12, ParentCommentId = 4, IsAuthorReply = true, BodyHtml = "<p>Thanks!</p>" };
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new CommentReplied(reply, [4])));

        var result = await CreateService(handler).ReplyAsync(4, new CommentReplyRequest { Body = "Thanks!" });

        var request = handler.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri!.AbsolutePath).IsEqualTo("/api/admin/comments/4/reply");
        await Assert.That((await request.Content!.ReadFromJsonAsync<CommentReplyRequest>())!.Body).IsEqualTo("Thanks!");
        var replied = result as CommentReplied;
        await Assert.That(replied).IsNotNull();
        await Assert.That(replied!.Reply.Id).IsEqualTo(12);
        await Assert.That(replied.ApprovedCommentIds).IsEquivalentTo([4]);
    }

    /// <summary>A 404 and a 400 map to the same results the server implementation returns.</summary>
    [Test]
    public async Task ReplyAsync_MapsNotFoundAndValidation()
    {
        var missing = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var invalid = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, new
        {
            title = "One or more validation errors occurred.",
            errors = new Dictionary<string, string[]> { ["Body"] = ["'Reply' must not be empty."] }
        }));

        var notFound = await CreateService(missing).ReplyAsync(4, new CommentReplyRequest());
        var rejected = await CreateService(invalid).ReplyAsync(4, new CommentReplyRequest());

        await Assert.That(notFound).IsTypeOf<CommentReplyNotFound>();
        await Assert.That(((CommentReplyInvalid)rejected).Errors["Body"]).IsEquivalentTo(["'Reply' must not be empty."]);
    }

    private static ClientCommentModerationService CreateService(StubHttpHandler handler)
    {
        return new ClientCommentModerationService(handler.CreateClient());
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}
