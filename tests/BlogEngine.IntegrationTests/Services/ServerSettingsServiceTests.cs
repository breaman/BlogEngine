using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests the server <see cref="ISettingsService"/> against the migrated database: the seeded row, the
/// cache, eviction on save, and validation.
/// </summary>
/// <remarks>
/// The settings row and the cache are shared by the whole session, so these tests never run in parallel
/// with each other and each one restores the settings it changed.
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(SiteSettings))]
public class ServerSettingsServiceTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The migration seeds one row holding the documented defaults.</summary>
    [Test]
    public async Task GetAsync_ReturnsSeededDefaults()
    {
        var settings = await GetSettingsAsync();

        await Assert.That(settings.PostsPerPage).IsEqualTo(10);
        await Assert.That(settings.FeedContentMode).IsEqualTo(FeedContentMode.FullContent);
        await Assert.That(settings.CommentsEnabled).IsTrue();
        await Assert.That(settings.RequireCommentApproval).IsTrue();
        await Assert.That(settings.AutoApproveReturningCommenters).IsFalse();
        await Assert.That(settings.CloseCommentsAfterDays).IsEqualTo(0);
        await Assert.That(settings.MaxCommentLinks).IsEqualTo(2);
        await Assert.That(settings.ShowCommentAvatars).IsFalse();
        await Assert.That(settings.MaxUploadSizeMegabytes).IsEqualTo(20);
        await Assert.That(settings.RenditionWidths).IsEquivalentTo([320, 640, 960, 1280, 1920]);
        await Assert.That(settings.TimeZoneId).IsEqualTo("UTC");
        await Assert.That(settings.AllowRegistration).IsFalse();
        await Assert.That(settings.DiscourageSearchEngines).IsFalse();
        await Assert.That(settings.SocialLinks).IsEmpty();
    }

    /// <summary>
    /// A change made behind the cache's back stays invisible (the value is cached), until a save through
    /// the service evicts the entry and the next read sees the saved values.
    /// </summary>
    [Test]
    public async Task SaveAsync_EvictsCachedSettings()
    {
        var original = await GetSettingsAsync();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.SiteSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.SiteTitle, "Changed behind the cache"));
            }

            var cached = await GetSettingsAsync();
            await Assert.That(cached.SiteTitle).IsEqualTo(original.SiteTitle);

            cached.SiteTitle = "Saved through the service";
            await SaveSettingsAsync(cached);

            var afterSave = await GetSettingsAsync();
            await Assert.That(afterSave.SiteTitle).IsEqualTo("Saved through the service");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
    }

    /// <summary>Each read returns its own copy, so editing one never leaks into the cache.</summary>
    [Test]
    public async Task GetAsync_ReturnsIndependentCopies()
    {
        var first = await GetSettingsAsync();
        first.SiteTitle = "Edited but never saved";

        var second = await GetSettingsAsync();

        await Assert.That(second.SiteTitle).IsNotEqualTo("Edited but never saved");
    }

    /// <summary>JSON-mapped columns (social links, rendition widths) survive a save and reload.</summary>
    [Test]
    public async Task SaveAsync_RoundTripsJsonColumns()
    {
        var original = await GetSettingsAsync();
        try
        {
            var changed = await GetSettingsAsync();
            changed.SocialLinks =
            [
                new SocialLinkDto { Network = "GitHub", Url = "https://github.com/example" },
                new SocialLinkDto { Network = "Mastodon", Url = "https://mastodon.social/@example" }
            ];
            changed.RenditionWidths = [640, 320, 640];
            await SaveSettingsAsync(changed);

            var reloaded = await GetSettingsAsync();

            await Assert.That(reloaded.SocialLinks.Select(l => l.Network)).IsEquivalentTo(["GitHub", "Mastodon"]);
            await Assert.That(reloaded.SocialLinks[1].Url).IsEqualTo("https://mastodon.social/@example");
            await Assert.That(reloaded.RenditionWidths).IsEquivalentTo([320, 640]);
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
    }

    /// <summary>An unknown time zone is rejected and nothing is saved.</summary>
    [Test]
    public async Task SaveAsync_RejectsUnknownTimeZone()
    {
        var settings = await GetSettingsAsync();
        var originalTimeZone = settings.TimeZoneId;
        settings.TimeZoneId = "Mars/Olympus_Mons";

        await Assert.That(async () => await SaveSettingsAsync(settings)).Throws<ValidationException>();

        var reloaded = await GetSettingsAsync();
        await Assert.That(reloaded.TimeZoneId).IsEqualTo(originalTimeZone);
    }

    /// <summary>A valid IANA time zone is accepted.</summary>
    [Test]
    public async Task SaveAsync_AcceptsIanaTimeZone()
    {
        var original = await GetSettingsAsync();
        try
        {
            var changed = await GetSettingsAsync();
            changed.TimeZoneId = "America/Chicago";
            await SaveSettingsAsync(changed);

            var reloaded = await GetSettingsAsync();
            await Assert.That(reloaded.TimeZoneId).IsEqualTo("America/Chicago");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
    }

    /// <summary>Reads settings in a fresh scope, as a request would.</summary>
    private async Task<SiteSettingsDto> GetSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    /// <summary>Saves settings in a fresh scope, as a request would.</summary>
    private async Task SaveSettingsAsync(SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }
}
