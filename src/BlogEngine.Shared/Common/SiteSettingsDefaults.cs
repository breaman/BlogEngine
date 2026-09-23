namespace BlogEngine.Shared.Common;

/// <summary>
/// Default values for the single <c>SiteSettings</c> row (design 13, task T0.8).
/// </summary>
/// <remarks>
/// Defined once so the entity initializers, the seeded row and any "reset to defaults" UI agree.
/// </remarks>
public static class SiteSettingsDefaults
{
    /// <summary>Site title until the author sets one.</summary>
    public const string SiteTitle = "My Blog";

    /// <summary>Posts shown per list page.</summary>
    public const int PostsPerPage = 10;

    /// <summary>
    /// IANA time zone used for URL dates and archives. UTC is the only zone guaranteed to exist on every
    /// host, so it is the safe default until the author picks their own.
    /// </summary>
    public const string TimeZoneId = "UTC";

    /// <summary>.NET custom date format for displayed dates, for example "September 22, 2026".</summary>
    public const string DateFormat = "MMMM d, yyyy";

    /// <summary>Comments older than this many days are closed; 0 means never.</summary>
    public const int CloseCommentsAfterDays = 0;

    /// <summary>Links a comment may contain before the spam guard starts adding to its score.</summary>
    public const int MaxCommentLinks = 2;

    /// <summary>Largest accepted upload, in megabytes.</summary>
    public const int MaxUploadSizeMegabytes = 20;

    /// <summary>Originals wider or taller than this are downscaled on upload; 0 keeps them at full size.</summary>
    public const int DownscaleOriginalsAbovePixels = 0;

    /// <summary>Widths of the pre-generated responsive renditions, in pixels.</summary>
    public static IReadOnlyList<int> RenditionWidths { get; } = [320, 640, 960, 1280, 1920];
}
