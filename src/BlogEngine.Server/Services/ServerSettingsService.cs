using System.ComponentModel.DataAnnotations;
using System.Globalization;

using BlogEngine.Data.Models;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="ISettingsService"/>: reads the single <see cref="SiteSettings"/> row
/// through <see cref="HybridCache"/> and evicts the cached copy whenever the settings are saved.
/// </summary>
/// <remarks>
/// <see cref="SiteSettingsDto"/> is mutable, so <see cref="HybridCache"/> hands every caller its own
/// deserialized copy; a caller editing its copy cannot corrupt the cached value.
/// </remarks>
public sealed class ServerSettingsService(ApplicationDbContext dbContext, HybridCache cache) : ISettingsService
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
        Validate(settings);

        var entity = await dbContext.SiteSettings
            .SingleOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, cancellationToken);
        if (entity is null)
        {
            // The migration seeds the row, but recreate it rather than fail if it was removed by hand.
            entity = new SiteSettings { Id = SiteSettings.SingletonId };
            dbContext.SiteSettings.Add(entity);
        }

        Apply(settings, entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Evict only after the save commits, so a concurrent read can't re-cache the old values after this.
        await cache.RemoveAsync(CacheKey, cancellationToken);
    }

    /// <summary>
    /// Reads the settings row, falling back to the defaults if it is missing so reads never fail.
    /// </summary>
    private async ValueTask<SiteSettingsDto> LoadAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.SiteSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, cancellationToken);

        return ToDto(entity ?? new SiteSettings { Id = SiteSettings.SingletonId });
    }

    /// <summary>
    /// Validates the data annotations on the settings and their social links, plus the rules annotations
    /// can't express: a known time zone, a usable date format and sensible rendition widths.
    /// </summary>
    /// <exception cref="ValidationException">Any rule fails; the message lists every failure.</exception>
    private static void Validate(SiteSettingsDto settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        // The validator does not recurse into collections.
        foreach (var link in settings.SocialLinks)
        {
            Validator.TryValidateObject(link, new ValidationContext(link), results, validateAllProperties: true);
        }

        if (!string.IsNullOrWhiteSpace(settings.TimeZoneId)
            && !TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimeZoneId, out _))
        {
            results.Add(new ValidationResult($"'{settings.TimeZoneId}' is not a known time zone.",
                [nameof(SiteSettingsDto.TimeZoneId)]));
        }

        if (!string.IsNullOrWhiteSpace(settings.DateFormat) && !IsUsableDateFormat(settings.DateFormat))
        {
            results.Add(new ValidationResult($"'{settings.DateFormat}' is not a valid date format.",
                [nameof(SiteSettingsDto.DateFormat)]));
        }

        if (settings.RenditionWidths.Count == 0 || settings.RenditionWidths.Any(w => w is < 16 or > 8192))
        {
            results.Add(new ValidationResult("Rendition widths must contain at least one width between 16 and 8192 pixels.",
                [nameof(SiteSettingsDto.RenditionWidths)]));
        }

        if (results.Count > 0)
        {
            throw new ValidationException(string.Join(" ", results.Select(r => r.ErrorMessage)));
        }
    }

    /// <summary>Checks that the format string formats a date without throwing.</summary>
    private static bool IsUsableDateFormat(string format)
    {
        try
        {
            _ = DateTimeOffset.UnixEpoch.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
