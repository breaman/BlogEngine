using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// The blog-wide settings (design 13), stored as a single row whose ID is always
/// <see cref="SingletonId"/>. Read it through <c>ISettingsService</c>, which caches it.
/// </summary>
public class SiteSettings : FingerPrintEntityBase
{
    /// <summary>ID of the only settings row; a check constraint rejects any other.</summary>
    public const int SingletonId = 1;

    // Identity

    /// <summary>Site title.</summary>
    public string SiteTitle { get; set; } = SiteSettingsDefaults.SiteTitle;

    /// <summary>Tagline shown under the title.</summary>
    public string? Tagline { get; set; }

    /// <summary>Default meta description for the site.</summary>
    public string? Description { get; set; }

    /// <summary>Author display name.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Author biography (Markdown).</summary>
    public string? AuthorBioMarkdown { get; set; }

    /// <summary>Media item used as the author avatar.</summary>
    public int? AuthorAvatarMediaId { get; set; }

    /// <summary>Media item used as the favicon.</summary>
    public int? FaviconMediaId { get; set; }

    /// <summary>Social profile links, in display order.</summary>
    public List<SocialLink> SocialLinks { get; set; } = [];

    // Reading

    /// <summary>Posts per list page.</summary>
    public int PostsPerPage { get; set; } = SiteSettingsDefaults.PostsPerPage;

    /// <summary>Whether feeds carry full content or summaries.</summary>
    public FeedContentMode FeedContentMode { get; set; } = FeedContentMode.FullContent;

    /// <summary>What the home page shows.</summary>
    public HomePageMode HomePageMode { get; set; } = HomePageMode.FeaturedThenLatest;

    // Localization

    /// <summary>IANA time zone for URL dates and archives.</summary>
    public string TimeZoneId { get; set; } = SiteSettingsDefaults.TimeZoneId;

    /// <summary>.NET custom date format for displayed dates.</summary>
    public string DateFormat { get; set; } = SiteSettingsDefaults.DateFormat;

    // Comments

    /// <summary>Master switch for reader comments.</summary>
    public bool CommentsEnabled { get; set; } = true;

    /// <summary>Whether new comments wait for moderation.</summary>
    public bool RequireCommentApproval { get; set; } = true;

    /// <summary>Whether commenters with a previously approved comment skip moderation.</summary>
    public bool AutoApproveReturningCommenters { get; set; }

    /// <summary>Close comments this many days after publishing; 0 means never.</summary>
    public int CloseCommentsAfterDays { get; set; } = SiteSettingsDefaults.CloseCommentsAfterDays;

    /// <summary>Links allowed in a comment before the spam score increases.</summary>
    public int MaxCommentLinks { get; set; } = SiteSettingsDefaults.MaxCommentLinks;

    /// <summary>Whether commenter avatars (Gravatar) are shown.</summary>
    public bool ShowCommentAvatars { get; set; }

    // Notifications

    /// <summary>Email the author when a comment is waiting for moderation.</summary>
    public bool NotifyOnPendingComment { get; set; }

    // Media

    /// <summary>Largest accepted upload, in megabytes.</summary>
    public int MaxUploadSizeMegabytes { get; set; } = SiteSettingsDefaults.MaxUploadSizeMegabytes;

    /// <summary>Downscale originals larger than this many pixels on either side; 0 disables downscaling.</summary>
    public int DownscaleOriginalsAbovePixels { get; set; } = SiteSettingsDefaults.DownscaleOriginalsAbovePixels;

    /// <summary>Widths of the responsive renditions, in pixels (stored as a JSON array).</summary>
    public List<int> RenditionWidths { get; set; } = [.. SiteSettingsDefaults.RenditionWidths];

    // SEO

    /// <summary>Media item used as the default social sharing image.</summary>
    public int? DefaultSocialImageMediaId { get; set; }

    /// <summary>Extra lines appended to <c>robots.txt</c>.</summary>
    public string? RobotsTxtExtras { get; set; }

    /// <summary>Ask search engines not to index the site.</summary>
    public bool DiscourageSearchEngines { get; set; }

    // Security

    /// <summary>Whether public account registration is open.</summary>
    public bool AllowRegistration { get; set; }
}
