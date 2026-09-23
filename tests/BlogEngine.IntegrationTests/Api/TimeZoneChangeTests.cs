using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests time zone change maintenance (design 7.1, Q10, T1.16): changing the blog's time zone recomputes the
/// dates in post URLs, and the old URLs answer 301 with the new ones.
/// </summary>
/// <remarks>
/// <para>
/// The time zone is picked so that it has the same offset as UTC right now but not at the test post's publish
/// time (Azores in the northern summer, London in the winter). Posts other tests publish "now" in parallel
/// therefore keep their dates, and their row versions, while this test changes the setting.
/// </para>
/// <para>Holds the settings lock, and restores the original time zone afterwards.</para>
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.SiteSettings)]
public class TimeZoneChangeTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>An old URL redirects to the post's re-dated URL, and a changed-back zone restores the original.</summary>
    [Test]
    public async Task ChangingTimeZone_RedirectsOldPostUrls()
    {
        var (timeZoneId, publishOn) = PickTimeZone(DateTimeOffset.UtcNow);
        var expectedDate = BlogTimeZone.ToLocalDate(publishOn, timeZoneId);
        var original = await GetSettingsAsync();
        await Assert.That(original.TimeZoneId).IsEqualTo(SiteSettingsDefaults.TimeZoneId);

        var post = await PublishAsync($"Time zone move {Guid.NewGuid():N}"[..28], publishOn);
        var utcPath = post.PublicPath!;
        await Assert.That(post.PublishedDateLocal).IsNotEqualTo(expectedDate);

        try
        {
            var changed = await GetSettingsAsync();
            changed.TimeZoneId = timeZoneId;
            await SaveSettingsAsync(changed);

            var moved = await GetPostAsync(post.Id);
            await Assert.That(moved.PublishedDateLocal).IsEqualTo(expectedDate);
            await Assert.That(moved.PublicPath).IsEqualTo(PostPaths.Post(expectedDate, post.Slug!));

            using var client = IdentityTestHelper.CreateClient(factory);
            using var response = await client.GetAsync(utcPath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
            await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(moved.PublicPath);
        }
        finally
        {
            await SaveSettingsAsync(original);
        }

        // Changing back moves the post home, and its re-dated URL now redirects to the original one.
        var restored = await GetPostAsync(post.Id);
        await Assert.That(restored.PublicPath).IsEqualTo(utcPath);

        using var restoredClient = IdentityTestHelper.CreateClient(factory);
        using var back = await restoredClient.GetAsync(PostPaths.Post(expectedDate, post.Slug!));
        await Assert.That(back.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(back.Headers.Location!.ToString()).IsEqualTo(utcPath);
    }

    /// <summary>
    /// A zone whose offset is zero at <paramref name="now"/> but not at the returned publish time, where the
    /// publish time is chosen to fall on a different local date than in UTC.
    /// </summary>
    private static (string TimeZoneId, DateTimeOffset PublishOn) PickTimeZone(DateTimeOffset now)
    {
        // Azores: UTC-1 in winter, UTC+0 in summer. 00:30 UTC in January is still the previous day there.
        var azores = TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Azores");
        if (azores.GetUtcOffset(now) == TimeSpan.Zero)
        {
            return ("Atlantic/Azores", new DateTimeOffset(2025, 1, 15, 0, 30, 0, TimeSpan.Zero));
        }

        // London: UTC+0 in winter, UTC+1 in summer. 23:30 UTC in July is already the next day there.
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        if (london.GetUtcOffset(now) == TimeSpan.Zero)
        {
            return ("Europe/London", new DateTimeOffset(2025, 7, 15, 23, 30, 0, TimeSpan.Zero));
        }

        throw new InvalidOperationException("Neither Atlantic/Azores nor Europe/London is at UTC+0 right now.");
    }

    private async Task<PostEditDto> PublishAsync(string title, DateTimeOffset publishOn)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();
        var created = (PostSaved)await posts.CreateAsync(new PostEditDto { Title = title });
        var published = await posts.PublishAsync(created.Post.Id, new PublishPostRequest { PublishOn = publishOn });

        return ((PostSaved)published).Post;
    }

    private async Task<PostEditDto> GetPostAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPostAdminService>().GetPostAsync(id))!;
    }

    private async Task<SiteSettingsDto> GetSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    private async Task SaveSettingsAsync(SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }
}
