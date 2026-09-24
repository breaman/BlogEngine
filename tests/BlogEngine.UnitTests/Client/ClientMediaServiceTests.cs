using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientMediaService"/> (T2.6): requests go to the media API routes, and status codes come back as
/// the same result values the server implementation returns.
/// </summary>
public class ClientMediaServiceTests
{
    /// <summary>List filters become query parameters.</summary>
    [Test]
    public async Task GetMediaAsync_SendsFilters()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new PagedResult<MediaItemDto>([], 0, 2, 24)));

        await CreateService(handler).GetMediaAsync(new MediaListQuery { Search = "sun set", Unused = true, Page = 2 });

        await Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery)
            .IsEqualTo("/api/admin/media?page=2&pageSize=24&search=sun%20set&unused=true");
    }

    /// <summary>A 409 on delete carries the posts that use the image.</summary>
    [Test]
    public async Task DeleteAsync_Conflict_ReturnsPostsInUse()
    {
        var posts = new List<MediaUsageDto> { new() { PostId = 3, Title = "Holiday" } };
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.Conflict, posts));

        var result = await CreateService(handler).DeleteAsync(5, force: false);

        await Assert.That(result).IsTypeOf<MediaInUse>();
        await Assert.That(((MediaInUse)result).Posts.Single().Title).IsEqualTo("Holiday");
        await Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery).IsEqualTo("/api/admin/media/5");
    }

    /// <summary>Forcing adds <c>?force=true</c>; 204 and 404 map to deleted and not found.</summary>
    [Test]
    [Arguments(HttpStatusCode.NoContent, typeof(MediaDeleted))]
    [Arguments(HttpStatusCode.NotFound, typeof(MediaDeleteNotFound))]
    public async Task DeleteAsync_Force_MapsStatus(HttpStatusCode status, Type expected)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));

        var result = await CreateService(handler).DeleteAsync(5, force: true);

        await Assert.That(result.GetType()).IsEqualTo(expected);
        await Assert.That(handler.Requests.Single().RequestUri!.Query).IsEqualTo("?force=true");
    }

    /// <summary>Edits post the operations and map 200, 400 and 404.</summary>
    [Test]
    public async Task EditAsync_MapsResults()
    {
        var ok = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new MediaItemDto { Id = 5, Version = 2 })));
        var invalid = CreateService(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest,
            new { errors = new Dictionary<string, string[]> { ["Rotate"] = ["Rotation must be 0, 90, 180 or 270 degrees."] } })));
        var missing = CreateService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var saved = await ok.EditAsync(5, new MediaEditOperations { Rotate = 90 });
        var rejected = await invalid.EditAsync(5, new MediaEditOperations { Rotate = 45 });
        var gone = await missing.EditAsync(5, new MediaEditOperations());

        await Assert.That(((MediaSaved)saved).Item.Version).IsEqualTo(2);
        await Assert.That(((MediaInvalid)rejected).Errors.Keys).IsEquivalentTo(["Rotate"]);
        await Assert.That(gone).IsTypeOf<MediaNotFound>();
    }

    /// <summary>Lookups repeat the <c>ids</c> parameter, and nothing is sent for an empty list.</summary>
    [Test]
    public async Task LookupAsync_RepeatsIds()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new List<MediaLookupItem>()));
        var service = CreateService(handler);

        await service.LookupAsync(["aaaaaaaaaaaa", "bbbbbbbbbbbb"]);
        await service.LookupAsync([]);

        await Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery)
            .IsEqualTo("/api/admin/media/lookup?ids=aaaaaaaaaaaa&ids=bbbbbbbbbbbb");
    }

    private static ClientMediaService CreateService(StubHttpHandler handler)
    {
        return new ClientMediaService(handler.CreateClient());
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}
