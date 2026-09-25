using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientTagService"/>: tag management requests go to the admin API routes, and status codes come back as
/// the same <see cref="TagResult"/> values the server implementation returns (T4.22).
/// </summary>
public class ClientTagServiceTests
{
    /// <summary>The full list comes from <c>/tags/all</c>.</summary>
    [Test]
    public async Task GetAllAsync_ReadsAllTags()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new[] { new TagAdminDto { Id = 1, Name = "C#", PostCount = 2 } }));

        var tags = await CreateService(handler).GetAllAsync();

        await Assert.That(tags.Single().Name).IsEqualTo("C#");
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/tags/all");
    }

    /// <summary>A rename is a PUT, and the saved tag comes back.</summary>
    [Test]
    public async Task UpdateAsync_Ok_ReturnsSavedTag()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new TagAdminDto { Id = 4, Name = "C#", Slug = "csharp" }));

        var result = await CreateService(handler).UpdateAsync(4, new UpdateTagRequest { Name = "C#" });

        await Assert.That(result).IsTypeOf<TagSaved>();
        await Assert.That(((TagSaved)result).Tag.Slug).IsEqualTo("csharp");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Put);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/tags/4");
    }

    /// <summary>A merge posts to <c>/tags/{id}/merge/{targetId}</c>.</summary>
    [Test]
    public async Task MergeAsync_PostsToMergeRoute()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new TagAdminDto { Id = 9, Name = "C#" }));

        var result = await CreateService(handler).MergeAsync(3, 9);

        await Assert.That(result).IsTypeOf<TagSaved>();
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/tags/3/merge/9");
    }

    /// <summary>204 and 404 map to the deleted and not-found results.</summary>
    [Test]
    [Arguments(HttpStatusCode.NoContent, typeof(TagDeleted))]
    [Arguments(HttpStatusCode.NotFound, typeof(TagNotFound))]
    public async Task DeleteAsync_MapsStatusCodes(HttpStatusCode status, Type expected)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));

        var result = await CreateService(handler).DeleteAsync(5);

        await Assert.That(result.GetType()).IsEqualTo(expected);
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Delete);
    }

    /// <summary>A 409 carries the problem's detail as the conflict message.</summary>
    [Test]
    public async Task DeleteAsync_Conflict_ReturnsProblemDetail()
    {
        var problem = new { title = "The tag can't be changed that way.", detail = "'C#' is used by 2 posts." };
        var service = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.Conflict, problem)));

        var result = await service.DeleteAsync(5);

        await Assert.That(result).IsTypeOf<TagConflict>();
        await Assert.That(((TagConflict)result).Message).IsEqualTo("'C#' is used by 2 posts.");
    }

    /// <summary>A validation problem's field errors come back keyed by property.</summary>
    [Test]
    public async Task UpdateAsync_ValidationProblem_ReturnsFieldErrors()
    {
        var problem = new { errors = new Dictionary<string, string[]> { ["Slug"] = ["Another tag already uses the slug 'csharp'."] } };
        var service = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, problem)));

        var result = await service.UpdateAsync(4, new UpdateTagRequest { Name = "C#", Slug = "csharp" });

        await Assert.That(result).IsTypeOf<TagInvalid>();
        await Assert.That(((TagInvalid)result).Errors["Slug"]).IsEquivalentTo(["Another tag already uses the slug 'csharp'."]);
    }

    private static ClientTagService CreateService(StubHttpHandler handler)
    {
        return new ClientTagService(handler.CreateClient());
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}
