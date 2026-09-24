using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientPageAdminService"/> (T4.10): it calls <c>/api/admin/pages</c> and maps status codes back to
/// the same <see cref="PageSaveResult"/> values the server implementation returns.
/// </summary>
public class ClientPageAdminServiceTests
{
    /// <summary>The list is read from the API.</summary>
    [Test]
    public async Task GetPagesAsync_ReturnsPages()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new[] { new PageSummaryDto { Id = 1, Title = "About" } }));

        var pages = await new ClientPageAdminService(handler.CreateClient()).GetPagesAsync();

        await Assert.That(pages.Single().Title).IsEqualTo("About");
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/pages");
    }

    /// <summary>A missing page is null, not an exception.</summary>
    [Test]
    public async Task GetPageAsync_NotFound_ReturnsNull()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.That(await new ClientPageAdminService(handler.CreateClient()).GetPageAsync(5)).IsNull();
    }

    /// <summary>Create posts the page and returns the saved one.</summary>
    [Test]
    public async Task CreateAsync_Created_ReturnsSaved()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.Created, new PageEditDto { Id = 9, Title = "About", Slug = "about" }));

        var result = await new ClientPageAdminService(handler.CreateClient()).CreateAsync(new PageEditDto { Title = "About" });

        await Assert.That(result).IsTypeOf<PageSaved>();
        await Assert.That(((PageSaved)result).Page.Slug).IsEqualTo("about");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
    }

    /// <summary>Publishing and unpublishing post to their own endpoints without a body.</summary>
    [Test]
    public async Task PublishAndUnpublish_PostWithoutBody()
    {
        var handler = new StubHttpHandler(request => JsonResponse(HttpStatusCode.OK, new PageEditDto
        {
            Id = 3,
            Status = request.RequestUri!.AbsolutePath.EndsWith("/publish", StringComparison.Ordinal) ? PostStatus.Published : PostStatus.Draft
        }));
        var service = new ClientPageAdminService(handler.CreateClient());

        var published = await service.PublishAsync(3);
        var unpublished = await service.UnpublishAsync(3);

        await Assert.That(((PageSaved)published).Page.Status).IsEqualTo(PostStatus.Published);
        await Assert.That(((PageSaved)unpublished).Page.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(handler.Requests.Select(r => r.RequestUri!.AbsolutePath))
            .IsEquivalentTo(["/api/admin/pages/3/publish", "/api/admin/pages/3/unpublish"]);
        await Assert.That(handler.Requests.All(r => r.Content is null)).IsTrue();
    }

    /// <summary>A validation problem becomes <see cref="PageInvalid"/> with the field errors; a 404 <see cref="PageNotFound"/>.</summary>
    [Test]
    public async Task UpdateAsync_MapsBadRequestAndNotFound()
    {
        var problem = new { errors = new Dictionary<string, string[]> { ["Slug"] = ["'admin' is used by the site itself."] } };
        var invalidHandler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, problem));
        var missingHandler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var invalid = await new ClientPageAdminService(invalidHandler.CreateClient()).UpdateAsync(1, new PageEditDto { Title = "x" });
        var missing = await new ClientPageAdminService(missingHandler.CreateClient()).UpdateAsync(1, new PageEditDto { Title = "x" });

        await Assert.That(((PageInvalid)invalid).Errors["Slug"].Single()).IsEqualTo("'admin' is used by the site itself.");
        await Assert.That(missing).IsTypeOf<PageNotFound>();
    }

    /// <summary>Delete reports whether the page existed.</summary>
    [Test]
    public async Task DeleteAsync_ReportsExistence()
    {
        var deleted = await new ClientPageAdminService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)).CreateClient()).DeleteAsync(1);
        var missing = await new ClientPageAdminService(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).CreateClient()).DeleteAsync(1);

        await Assert.That(deleted).IsTrue();
        await Assert.That(missing).IsFalse();
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}
