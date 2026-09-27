using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Validation;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// The blog-wide settings (design 13), as read and edited through <see cref="Services.ISettingsService"/>.
/// </summary>
/// <remarks>
/// A mutable class rather than a record so the settings form can bind to it directly. Instances handed
/// out by the settings service are copies, so changing one never affects other callers until it is saved.
/// The rules, such as lengths, ranges and a known time zone, are in <see cref="SiteSettingsValidator"/>.
/// </remarks>
public sealed class SiteSettingsDto
{
    // Identity

    /// <summary>Site title shown in the header, browser tab and feeds.</summary>
    public string SiteTitle { get; set; } = string.Empty;

    /// <summary>Short tagline shown under the title.</summary>
    public string? Tagline { get; set; }

    /// <summary>Site description used as the default meta description.</summary>
    public string? Description { get; set; }

    /// <summary>Author display name used in bylines and feeds.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Author biography (Markdown).</summary>
    public string? AuthorBioMarkdown { get; set; }

    /// <summary>Media library item used as the author avatar.</summary>
    public int? AuthorAvatarMediaId { get; set; }

    /// <summary>Media library item used as the favicon.</summary>
    public int? FaviconMediaId { get; set; }

    /// <summary>Social profile links, in display order.</summary>
    public List<SocialLinkDto> SocialLinks { get; set; } = [];

    // Reading

    /// <summary>Posts per list page.</summary>
    public int PostsPerPage { get; set; }

    /// <summary>Whether feeds carry full content or only summaries.</summary>
    public FeedContentMode FeedContentMode { get; set; }

    /// <summary>What the home page shows.</summary>
    public HomePageMode HomePageMode { get; set; }

    // Localization

    /// <summary>IANA time zone for URL dates and archives, for example <c>America/Chicago</c>.</summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>.NET custom date format for displayed dates.</summary>
    public string DateFormat { get; set; } = string.Empty;

    // Comments

    /// <summary>Master switch for reader comments.</summary>
    public bool CommentsEnabled { get; set; }

    /// <summary>Whether new comments wait for moderation.</summary>
    public bool RequireCommentApproval { get; set; }

    /// <summary>Whether commenters with a previously approved comment skip moderation.</summary>
    public bool AutoApproveReturningCommenters { get; set; }

    /// <summary>Close comments this many days after publishing; 0 means never.</summary>
    public int CloseCommentsAfterDays { get; set; }

    /// <summary>Links allowed in a comment before the spam score increases.</summary>
    public int MaxCommentLinks { get; set; }

    /// <summary>Whether commenter avatars (Gravatar) are shown.</summary>
    public bool ShowCommentAvatars { get; set; }

    // Notifications

    /// <summary>Email the author when a comment is waiting for moderation.</summary>
    public bool NotifyOnPendingComment { get; set; }

    // Media

    /// <summary>Largest accepted upload, in megabytes.</summary>
    public int MaxUploadSizeMegabytes { get; set; }

    /// <summary>Downscale originals larger than this many pixels on either side; 0 disables downscaling.</summary>
    public int DownscaleOriginalsAbovePixels { get; set; }

    /// <summary>Widths of the responsive renditions, in pixels.</summary>
    public List<int> RenditionWidths { get; set; } = [];

    // SEO

    /// <summary>Media library item used as the default social sharing image.</summary>
    public int? DefaultSocialImageMediaId { get; set; }

    /// <summary>Extra lines appended to <c>robots.txt</c>.</summary>
    public string? RobotsTxtExtras { get; set; }

    /// <summary>Ask search engines not to index the site (<c>noindex</c> everywhere); useful before launch.</summary>
    public bool DiscourageSearchEngines { get; set; }

    // Security

    /// <summary>Whether public account registration is open.</summary>
    public bool AllowRegistration { get; set; }
}