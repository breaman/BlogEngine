using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests private preview links, <c>/preview/{token}</c> (design 6.7, 7.1, A14, T4.4): a draft is readable through a live
/// link with a "Preview" banner and <c>noindex</c>, and expired, revoked or unknown tokens are a 404.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PreviewLinkTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A draft renders through its link, marked as a preview and hidden from search engines, while its public URL stays a 404.</summary>
    [Test]
    public async Task ValidLink_RendersDraft_WithNoindex()
    {
        var marker = PublicTestPosts.Token();
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto
        {
            Title = $"Preview me {marker}",
            ContentMarkdown = $"Draft **body** {marker}."
        });
        var link = await CreateLinkAsync(draft.Id);
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(link.Path);
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("X-Robots-Tag").Single()).Contains("noindex");
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(html).Contains("<meta name=\"robots\" content=\"noindex, nofollow\"");
        await Assert.That(html).Contains($"Preview me {marker}</h1>");
        await Assert.That(html).Contains($"Draft <strong>body</strong> {marker}.");
        await Assert.That(WebUtility.HtmlDecode(html)).Contains("This post isn't published yet.");
        await Assert.That(html).DoesNotContain("rel=\"canonical\"");
    }

    /// <summary>A revoked link is a 404 straight away.</summary>
    [Test]
    public async Task RevokedLink_Returns404()
    {
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Revoked {PublicTestPosts.Token()}" });
        var link = await CreateLinkAsync(draft.Id);
        using var client = IdentityTestHelper.CreateClient(factory);
        using var before = await client.GetAsync(link.Path);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPreviewLinkService>().RevokeAsync(draft.Id, link.Id);
        }

        using var after = await client.GetAsync(link.Path);

        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>An expired link is a 404, and no longer listed for the editor.</summary>
    [Test]
    public async Task ExpiredLink_Returns404()
    {
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Expired {PublicTestPosts.Token()}" });
        var link = await CreateLinkAsync(draft.Id);
        IReadOnlyList<PreviewLinkDto>? listed;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.PreviewTokens.Where(t => t.Id == link.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresOn, DateTimeOffset.UtcNow.AddMinutes(-1)));
            listed = await scope.ServiceProvider.GetRequiredService<IPreviewLinkService>().GetLinksAsync(draft.Id);
        }

        using var client = IdentityTestHelper.CreateClient(factory);
        using var response = await client.GetAsync(link.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(listed).IsEmpty();
    }

    /// <summary>A link to a post in the trash stops working, and made-up tokens are a 404.</summary>
    [Test]
    public async Task TrashedPostAndUnknownToken_Return404()
    {
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Trashed {PublicTestPosts.Token()}" });
        var link = await CreateLinkAsync(draft.Id);
        await PublicTestPosts.TrashAsync(factory, draft.Id);
        using var client = IdentityTestHelper.CreateClient(factory);

        using var trashed = await client.GetAsync(link.Path);
        using var unknown = await client.GetAsync("/preview/not-a-real-token");

        await Assert.That(trashed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>A scheduled post's preview says when it goes live.</summary>
    [Test]
    public async Task ScheduledPost_PreviewSaysWhen()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Soon {PublicTestPosts.Token()}" },
            DateTimeOffset.UtcNow.AddDays(2));
        var link = await CreateLinkAsync(post.Id);
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, link.Path);

        await Assert.That(html).Contains("This post is scheduled for");
    }

    private async Task<PreviewLinkDto> CreateLinkAsync(int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPreviewLinkService>().CreateAsync(postId, new CreatePreviewLinkRequest()))!;
    }
}