using System.Net;
using System.Net.Http.Headers;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;

using Microsoft.Extensions.DependencyInjection;

using SixLabors.ImageSharp;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the public media endpoint <c>/media/{publicId}/{fileName}</c> (design 9.4, T2.4): content type, cache
/// headers for current and other versions, <c>nosniff</c>, ETags with 304, and 404s.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class MediaEndpointTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The current version is cacheable forever, with the detected type and nosniff.</summary>
    [Test]
    public async Task CurrentVersion_IsImmutable()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"serve-{MediaTestFiles.Token()}.png");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(item.Url);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("image/png");
        var cacheControl = response.Headers.CacheControl!;
        await Assert.That((cacheControl.Public, cacheControl.MaxAge)).IsEqualTo((true, TimeSpan.FromDays(365)));
        await Assert.That(cacheControl.Extensions.Select(e => e.Name)).Contains("immutable");
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.ETag!.Tag).IsEqualTo($"\"{item.PublicId}-1\"");
        await Assert.That(Image.Identify(bytes).Width).IsEqualTo(item.Width);
    }

    /// <summary>Without <c>?v=</c>, or with an old one, the response must be revalidated.</summary>
    [Test]
    [Arguments("")]
    [Arguments("?v=7")]
    [Arguments("?v=abc")]
    public async Task OtherVersions_Revalidate(string query)
    {
        var item = await MediaTestFiles.AddAsync(factory, $"revalidate-{MediaTestFiles.Token()}.png");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(item.Path + query);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cacheControl = response.Headers.CacheControl!;
        await Assert.That((cacheControl.Public, cacheControl.MustRevalidate, cacheControl.MaxAge)).IsEqualTo((true, true, TimeSpan.FromMinutes(5)));
        await Assert.That(cacheControl.Extensions.Select(e => e.Name)).DoesNotContain("immutable");
    }

    /// <summary>A matching If-None-Match answers 304 without a body; after an edit the old ETag no longer matches.</summary>
    [Test]
    public async Task MatchingETag_Returns304_UntilEdited()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"etag-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(40, 20));
        using var client = factory.CreateClient();
        using var first = await client.GetAsync(item.Path);
        var etag = first.Headers.ETag!;

        using var notModified = await GetWithETagAsync(client, item.Path, etag);
        await EditAsync(item.Id, new MediaEditOperations { Rotate = 90 });
        using var changed = await GetWithETagAsync(client, item.Path, etag);

        await Assert.That(notModified.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That((await notModified.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Image.Identify(await changed.Content.ReadAsByteArrayAsync()).Width).IsEqualTo(20);
        await Assert.That(changed.Headers.ETag!.Tag).IsEqualTo($"\"{item.PublicId}-2\"");
    }

    /// <summary>Unknown ids, wrong file names and malformed ids are 404.</summary>
    [Test]
    public async Task UnknownMedia_Returns404()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"notfound-{MediaTestFiles.Token()}.png");
        using var client = factory.CreateClient();

        using var unknownId = await client.GetAsync($"/media/zzzzzzzzzzzz/{item.FileName}");
        using var wrongName = await client.GetAsync($"/media/{item.PublicId}/other.png");
        using var shortId = await client.GetAsync("/media/abc/x.png");

        await Assert.That(unknownId.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(wrongName.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(shortId.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> GetWithETagAsync(HttpClient client, string path, EntityTagHeaderValue etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(etag);
        return client.SendAsync(request);
    }

    private async Task EditAsync(int id, MediaEditOperations operations)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<BlogEngine.Shared.Services.IMediaService>().EditAsync(id, operations);
        if (result is not MediaSaved)
        {
            throw new InvalidOperationException($"Editing media {id} failed: {result}");
        }
    }
}