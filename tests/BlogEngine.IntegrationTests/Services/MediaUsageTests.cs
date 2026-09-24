using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests media usage tracking (T2.12): saving a post rebuilds its <c>PostMedia</c> rows from the library URLs in its
/// Markdown, which drives "Used in N posts" and the unused filter.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class MediaUsageTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Adding an image to a post counts it; removing it again frees it.</summary>
    [Test]
    public async Task AddingAndRemovingImage_UpdatesUsage()
    {
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"usage-{marker}.png");
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto
        {
            Title = $"Usage {marker}",
            ContentMarkdown = $"Text.\n\n![Squares]({item.Path} \"Caption\"){{.img-small}}"
        });

        var used = await LoadAsync(item.Id);
        var unusedWhileUsed = await UnusedIdsAsync(marker);

        post.ContentMarkdown = "Text only.";
        await PublicTestPosts.SaveAsync(factory, posts => posts.UpdateAsync(post.Id, post));
        var freed = await LoadAsync(item.Id);
        var unusedAfter = await UnusedIdsAsync(marker);

        await Assert.That(used.UsageCount).IsEqualTo(1);
        await Assert.That(used.UsedIn.Single().Title).IsEqualTo(post.Title);
        await Assert.That(unusedWhileUsed).DoesNotContain(item.Id);
        await Assert.That(freed.UsageCount).IsEqualTo(0);
        await Assert.That(unusedAfter).Contains(item.Id);
    }

    /// <summary>
    /// Absolute URLs, the <c>?v=</c> the rendered HTML uses and raw HTML all count, and each post counts once however
    /// often it shows the image.
    /// </summary>
    [Test]
    public async Task EveryReferenceForm_CountsOncePerPost()
    {
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"forms-{marker}.png");
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto
        {
            Title = $"Forms A {marker}",
            ContentMarkdown = $"![a]({item.Url})\n\n![b](https://blog.example.com{item.Path})"
        });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto
        {
            Title = $"Forms B {marker}",
            ContentMarkdown = $"<img src=\"{item.Path}\" alt=\"raw\">"
        });

        var loaded = await LoadAsync(item.Id);

        await Assert.That(loaded.UsageCount).IsEqualTo(2);
    }

    /// <summary>A post in the trash still uses its images: restoring it must not bring back a broken image.</summary>
    [Test]
    public async Task TrashedPost_StillCounts()
    {
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"trash-{marker}.png");
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Trash {marker}", ContentMarkdown = $"![x]({item.Path})" });

        await PublicTestPosts.TrashAsync(factory, post.Id);
        var loaded = await LoadAsync(item.Id);

        await Assert.That(loaded.UsageCount).IsEqualTo(1);
        await Assert.That(loaded.UsedIn.Single().IsInTrash).IsTrue();
        await Assert.That(await UnusedIdsAsync(marker)).DoesNotContain(item.Id);
    }

    /// <summary>A post saved before its image was uploaded picks it up on the next save.</summary>
    [Test]
    public async Task UsageIsRebuiltOnEverySave()
    {
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"later-{marker}.png");
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Later {marker}", ContentMarkdown = "Nothing yet." });

        post.ContentMarkdown = $"Now: ![x]({item.Path})";
        await PublicTestPosts.SaveAsync(factory, posts => posts.AutosaveAsync(post.Id, post));

        await Assert.That((await LoadAsync(item.Id)).UsageCount).IsEqualTo(1);
    }

    private async Task<MediaItemDto> LoadAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IMediaService>().GetMediaItemAsync(id))!;
    }

    private async Task<List<int>> UnusedIdsAsync(string search)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediaService>()
            .GetMediaAsync(new MediaListQuery { Search = search, Unused = true, PageSize = MediaListQuery.MaxPageSize });
        return [.. result.Items.Select(i => i.Id)];
    }
}
