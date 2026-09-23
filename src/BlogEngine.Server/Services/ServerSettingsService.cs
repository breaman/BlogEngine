using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Validation;

using FluentValidation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="ISettingsService"/>: reads the single <see cref="SiteSettings"/> row
/// through <see cref="HybridCache"/> and evicts the cached copy whenever the settings are saved.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SiteSettingsDto"/> is mutable, so <see cref="HybridCache"/> hands every caller its own
/// deserialized copy; a caller editing its copy cannot corrupt the cached value.
/// </para>
/// <para>
/// Saves are checked with <see cref="SiteSettingsValidator"/> first, so an invalid value never reaches the
/// database even if the caller skipped client-side validation.
/// </para>
/// <para>
/// Changing the time zone recomputes every post's URL date and redirects the URLs that moved
/// (<see cref="PostLocalDateMaintenance"/>, T1.16), in the same transaction as the settings.
/// </para>
/// <para>
/// A save evicts the settings and, through <see cref="CacheInvalidator"/>, every public page, feed and
/// <c>robots.txt</c> response they affect, so readers see the new values on their next request.
/// </para>
/// <para>
/// Cache misses load through their own scope rather than the request's <see cref="ApplicationDbContext"/>:
/// during static SSR the layout and the page initialize concurrently, so a settings read in the layout would
/// otherwise collide with the page's own queries on the shared context. It also keeps a load that
/// <see cref="HybridCache"/> shares between concurrent requests independent of any one request's lifetime.
/// </para>
/// </remarks>
public sealed class ServerSettingsService(
    ApplicationDbContext dbContext,
    HybridCache cache,
    IServiceScopeFactory scopeFactory,
    IValidator<SiteSettingsDto> validator,
    CacheInvalidator cacheInvalidator,
    ILogger<ServerSettingsService> logger) : ISettingsService
{
    /// <summary>Cache key of the settings entry.</summary>
    public const string CacheKey = "site-settings";

    /// <summary>Cache tag of the settings entry, so a broader invalidation can evict it by tag.</summary>
    public const string CacheTag = "settings";

    private static readonly string[] CacheTags = [CacheTag];

    /// <inheritdoc />
    public async Task<SiteSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKey, LoadAsync, tags: CacheTags,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SiteSettingsDto settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await validator.ValidateAndThrowAsync(settings, cancellationToken);

        var entity = await dbContext.SiteSettings
            .SingleOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, cancellationToken);
        if (entity is null)
        {
            // The migration seeds the row, but recreate it rather than fail if it was removed by hand.
            entity = new SiteSettings { Id = SiteSettings.SingletonId };
            dbContext.SiteSettings.Add(entity);
        }

        var previousTimeZoneId = entity.TimeZoneId;
        Apply(settings, entity);

        var redatedPosts = 0;
        if (!string.Equals(previousTimeZoneId, entity.TimeZoneId, StringComparison.Ordinal))
        {
            redatedPosts = await PostLocalDateMaintenance.RecomputeAsync(dbContext, entity.TimeZoneId, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (redatedPosts > 0)
        {
            logger.LogInformation("Time zone changed from {OldTimeZone} to {NewTimeZone}; {PostCount} posts got new URL dates.",
                previousTimeZoneId, entity.TimeZoneId, redatedPosts);
        }

        // Evict only after the save commits, so a concurrent read can't re-cache the old values after this.
        await cacheInvalidator.SettingsChangedAsync(CacheKey);
    }

    /// <summary>
    /// Reads the settings row, falling back to the defaults if it is missing so reads never fail.
    /// </summary>
    private async ValueTask<SiteSettingsDto> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var loadContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entity = await loadContext.SiteSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, cancellationToken);

        return ToDto(entity ?? new SiteSettings { Id = SiteSettings.SingletonId });
    }

    /// <summary>Copies the entity's values into a new DTO.</summary>
    private static SiteSettingsDto ToDto(SiteSettings entity)
    {
        return new SiteSettingsDto
        {
            SiteTitle = entity.SiteTitle,
            Tagline = entity.Tagline,
            Description = entity.Description,
            AuthorName = entity.AuthorName,
            AuthorBioMarkdown = entity.AuthorBioMarkdown,
            AuthorAvatarMediaId = entity.AuthorAvatarMediaId,
            FaviconMediaId = entity.FaviconMediaId,
            SocialLinks = [.. entity.SocialLinks.Select(l => new SocialLinkDto { Network = l.Network, Url = l.Url })],
            PostsPerPage = entity.PostsPerPage,
            FeedContentMode = entity.FeedContentMode,
            HomePageMode = entity.HomePageMode,
            TimeZoneId = entity.TimeZoneId,
            DateFormat = entity.DateFormat,
            CommentsEnabled = entity.CommentsEnabled,
            RequireCommentApproval = entity.RequireCommentApproval,
            AutoApproveReturningCommenters = entity.AutoApproveReturningCommenters,
            CloseCommentsAfterDays = entity.CloseCommentsAfterDays,
            MaxCommentLinks = entity.MaxCommentLinks,
            ShowCommentAvatars = entity.ShowCommentAvatars,
            NotifyOnPendingComment = entity.NotifyOnPendingComment,
            MaxUploadSizeMegabytes = entity.MaxUploadSizeMegabytes,
            DownscaleOriginalsAbovePixels = entity.DownscaleOriginalsAbovePixels,
            RenditionWidths = [.. entity.RenditionWidths],
            DefaultSocialImageMediaId = entity.DefaultSocialImageMediaId,
            RobotsTxtExtras = entity.RobotsTxtExtras,
            DiscourageSearchEngines = entity.DiscourageSearchEngines,
            AllowRegistration = entity.AllowRegistration
        };
    }

    /// <summary>Copies the DTO's values onto the tracked entity, normalizing blank optional text to null.</summary>
    private static void Apply(SiteSettingsDto dto, SiteSettings entity)
    {
        entity.SiteTitle = dto.SiteTitle.Trim();
        entity.Tagline = NullIfBlank(dto.Tagline);
        entity.Description = NullIfBlank(dto.Description);
        entity.AuthorName = NullIfBlank(dto.AuthorName);
        entity.AuthorBioMarkdown = NullIfBlank(dto.AuthorBioMarkdown);
        entity.AuthorAvatarMediaId = dto.AuthorAvatarMediaId;
        entity.FaviconMediaId = dto.FaviconMediaId;
        entity.SocialLinks = [.. dto.SocialLinks.Select(l => new SocialLink { Network = l.Network.Trim(), Url = l.Url.Trim() })];
        entity.PostsPerPage = dto.PostsPerPage;
        entity.FeedContentMode = dto.FeedContentMode;
        entity.HomePageMode = dto.HomePageMode;
        entity.TimeZoneId = dto.TimeZoneId.Trim();
        entity.DateFormat = dto.DateFormat;
        entity.CommentsEnabled = dto.CommentsEnabled;
        entity.RequireCommentApproval = dto.RequireCommentApproval;
        entity.AutoApproveReturningCommenters = dto.AutoApproveReturningCommenters;
        entity.CloseCommentsAfterDays = dto.CloseCommentsAfterDays;
        entity.MaxCommentLinks = dto.MaxCommentLinks;
        entity.ShowCommentAvatars = dto.ShowCommentAvatars;
        entity.NotifyOnPendingComment = dto.NotifyOnPendingComment;
        entity.MaxUploadSizeMegabytes = dto.MaxUploadSizeMegabytes;
        entity.DownscaleOriginalsAbovePixels = dto.DownscaleOriginalsAbovePixels;
        entity.RenditionWidths = [.. dto.RenditionWidths.Distinct().Order()];
        entity.DefaultSocialImageMediaId = dto.DefaultSocialImageMediaId;
        entity.RobotsTxtExtras = NullIfBlank(dto.RobotsTxtExtras);
        entity.DiscourageSearchEngines = dto.DiscourageSearchEngines;
        entity.AllowRegistration = dto.AllowRegistration;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
