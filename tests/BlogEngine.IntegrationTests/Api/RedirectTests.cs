using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests that URLs of moved posts keep working over HTTP (design 6.7, P16, T1.5): after a published post's
/// slug changes, its old URL answers 301 to the new one.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class RedirectTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The old URL redirects permanently to the new one and the hit is counted.</summary>
    [Test]
    public async Task OldPostUrl_Returns301ToNewUrl()
    {
        var token = Guid.NewGuid().ToString("N")[..10];
        var published = await SaveAsync(async s =>
        {
            var created = (PostSaved)await s.CreateAsync(new PostEditDto { Title = $"Redirect me {token}" });
            return await s.PublishAsync(created.Post.Id, new PublishPostRequest());
        });
        var oldPath = published.PublicPath!;
        published.Slug = $"redirected-{token}";
        var moved = await SaveAsync(s => s.UpdateAsync(published.Id, published));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(oldPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(moved.PublicPath);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await dbContext.Redirects.Where(r => r.FromPath == oldPath).Select(r => r.HitCount).SingleAsync()).IsEqualTo(1);
    }

    /// <summary>The lookup ignores a trailing slash, and paths with no redirect still 404.</summary>
    [Test]
    public async Task UnknownUrl_Still404s()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync($"/posts/2001/01/01/nothing-here-{Guid.NewGuid():N}/");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private async Task<PostEditDto> SaveAsync(Func<IPostAdminService, Task<PostSaveResult>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return ((PostSaved)await action(scope.ServiceProvider.GetRequiredService<IPostAdminService>())).Post;
    }
}