using System.Net;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers for tests of the public site: creating and publishing posts through the real admin service, and
/// reading pages over HTTP.
/// </summary>
/// <remarks>
/// Every test in the session shares one database and runs in parallel, so the public lists contain other
/// tests' posts. Tests that need a list of known content publish into a year of their own from
/// <see cref="NextYear"/>, whose archive nothing else touches.
/// </remarks>
public static class PublicTestPosts
{
    // Years before any real post (tests publishing "now" use the current year; others use 2025).
    private static int lastYear = 1920;

    /// <summary>A year no other test in this session publishes into.</summary>
    public static int NextYear()
    {
        return Interlocked.Increment(ref lastYear);
    }

    /// <summary>A short random token for unique titles and tag names.</summary>
    public static string Token()
    {
        return Guid.NewGuid().ToString("N")[..10];
    }

    /// <summary>Noon UTC on a date, which is the same date in every time zone a test may switch to.</summary>
    public static DateTimeOffset Noon(int year, int month, int day)
    {
        return new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Creates a draft and returns it.</summary>
    public static async Task<PostEditDto> CreateDraftAsync(BlogEngineWebApplicationFactory factory, PostEditDto post)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();

        return Saved(await posts.CreateAsync(post));
    }

    /// <summary>Creates and publishes a post; <paramref name="publishOn"/> defaults to now.</summary>
    public static async Task<PostEditDto> PublishAsync(BlogEngineWebApplicationFactory factory, PostEditDto post,
        DateTimeOffset? publishOn = null)
    {
        var draft = await CreateDraftAsync(factory, post);

        await using var scope = factory.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();

        return Saved(await posts.PublishAsync(draft.Id, new PublishPostRequest { PublishOn = publishOn }));
    }

    /// <summary>Runs an admin operation on an existing post and returns the saved post.</summary>
    public static async Task<PostEditDto> SaveAsync(BlogEngineWebApplicationFactory factory,
        Func<IPostAdminService, Task<PostSaveResult>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return Saved(await action(scope.ServiceProvider.GetRequiredService<IPostAdminService>()));
    }

    /// <summary>Moves a post to the trash.</summary>
    public static async Task TrashAsync(BlogEngineWebApplicationFactory factory, int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        if (!await scope.ServiceProvider.GetRequiredService<IPostAdminService>().DeleteAsync(postId))
        {
            throw new InvalidOperationException($"Post {postId} was not found.");
        }
    }

    /// <summary>GETs <paramref name="path"/>, asserting a 200, and returns the body.</summary>
    public static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {path} returned {(int)response.StatusCode}, expected 200.");
        }

        return body;
    }

    private static PostEditDto Saved(PostSaveResult result)
    {
        return result is PostSaved saved
            ? saved.Post
            : throw new InvalidOperationException($"Expected the post to save, but got {result.GetType().Name}.");
    }
}