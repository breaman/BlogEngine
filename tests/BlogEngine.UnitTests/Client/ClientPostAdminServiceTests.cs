using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientPostAdminService"/>: requests go to the right admin API routes, and HTTP status
/// codes come back as the same <see cref="PostSaveResult"/> values the server implementation returns (T1.8).
/// </summary>
public class ClientPostAdminServiceTests
{
    /// <summary>A 200 carries the saved post back.</summary>
    [Test]
    public async Task UpdateAsync_Ok_ReturnsSavedPost()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new PostEditDto { Id = 7, Title = "Saved" }));
        var service = CreateService(handler);

        var result = await service.UpdateAsync(7, new PostEditDto { Title = "Saved" });

        await Assert.That(result).IsTypeOf<PostSaved>();
        await Assert.That(((PostSaved)result).Post.Title).IsEqualTo("Saved");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Put);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/posts/7");
    }

    /// <summary>404 and 409 map to the not-found and conflict results.</summary>
    [Test]
    [Arguments(HttpStatusCode.NotFound, typeof(PostNotFound))]
    [Arguments(HttpStatusCode.Conflict, typeof(PostConflict))]
    public async Task AutosaveAsync_MapsStatusCodes(HttpStatusCode status, Type expected)
    {
        var service = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(status)));

        var result = await service.AutosaveAsync(3, new PostEditDto { Title = "x" });

        await Assert.That(result.GetType()).IsEqualTo(expected);
    }

    /// <summary>A validation problem's field errors come back keyed by property.</summary>
    [Test]
    public async Task CreateAsync_ValidationProblem_ReturnsFieldErrors()
    {
        var problem = new { title = "One or more validation errors occurred.", errors = new Dictionary<string, string[]> { ["Title"] = ["A title is required."] } };
        var service = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, problem)));

        var result = await service.CreateAsync(new PostEditDto());

        await Assert.That(result).IsTypeOf<PostInvalid>();
        await Assert.That(((PostInvalid)result).Errors["Title"]).IsEquivalentTo(["A title is required."]);
    }

    /// <summary>A 400 without field errors (such as a rejected antiforgery token) becomes a form-level error.</summary>
    [Test]
    public async Task PublishAsync_ProblemWithoutErrors_ReturnsFormLevelError()
    {
        var problem = new { title = "Invalid antiforgery token", detail = "Reload the page and try again." };
        var service = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, problem)));

        var result = await service.PublishAsync(1, new PublishPostRequest());

        await Assert.That(((PostInvalid)result).Errors[string.Empty]).IsEquivalentTo(["Reload the page and try again."]);
    }

    /// <summary>Unexpected failures are not disguised as a result.</summary>
    [Test]
    public async Task UnpublishAsync_ServerError_Throws()
    {
        var service = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.That(async () => await service.UnpublishAsync(1, new UnpublishPostRequest())).Throws<HttpRequestException>();
    }

    /// <summary>A missing post reads as <see langword="null"/> and a missing delete target as <see langword="false"/>.</summary>
    [Test]
    public async Task GetAndDelete_NotFound_ReturnNullAndFalse()
    {
        var service = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        await Assert.That(await service.GetPostAsync(99)).IsNull();
        await Assert.That(await service.DeleteAsync(99)).IsFalse();
    }

    /// <summary>Restoring posts to <c>/posts/{id}/restore</c> and returns the restored draft; 404 is not found.</summary>
    [Test]
    public async Task RestoreAsync_MapsResponses()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new PostEditDto { Id = 5, Title = "Back", Status = PostStatus.Draft }));
        var missing = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var restored = await CreateService(handler).RestoreAsync(5);

        await Assert.That(restored).IsTypeOf<PostSaved>();
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/posts/5/restore");
        await Assert.That(await missing.RestoreAsync(5)).IsTypeOf<PostNotFound>();
    }

    /// <summary>Deleting permanently uses <c>DELETE /posts/{id}/permanent</c>; 404 means the post isn't in the trash.</summary>
    [Test]
    public async Task DeletePermanentlyAsync_MapsResponses()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var missing = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        await Assert.That(await CreateService(handler).DeletePermanentlyAsync(6)).IsTrue();
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/posts/6/permanent");
        await Assert.That(await missing.DeletePermanentlyAsync(6)).IsFalse();
    }

    /// <summary>Emptying the trash reports how many posts were deleted.</summary>
    [Test]
    public async Task EmptyTrashAsync_ReturnsDeletedCount()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new EmptyTrashResponse(3)));

        var deleted = await CreateService(handler).EmptyTrashAsync();

        await Assert.That(deleted).IsEqualTo(3);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/posts/empty-trash");
    }

    /// <summary>List filters are sent in the query string, escaped.</summary>
    [Test]
    public async Task GetPostsAsync_SendsFiltersInQueryString()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new PagedResult<PostSummaryDto>([], 0, 2, 5)));
        var service = CreateService(handler);

        await service.GetPostsAsync(new PostListQuery { Status = PostListStatus.Draft, Tag = "C#", Search = "a b", Page = 2, PageSize = 5 });

        var query = handler.Requests.Single().RequestUri!.Query;
        await Assert.That(query).IsEqualTo("?status=Draft&page=2&pageSize=5&tag=C%23&search=a%20b");
    }

    private static ClientPostAdminService CreateService(StubHttpHandler handler)
    {
        return new ClientPostAdminService(handler.CreateClient());
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}
