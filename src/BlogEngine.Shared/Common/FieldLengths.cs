namespace BlogEngine.Shared.Common;

/// <summary>
/// Canonical maximum storage lengths shared by EF Core configurations and API contracts.
/// Defined once here so a validation attribute and a column definition can never drift apart
/// (design section 6.9).
/// </summary>
public static class FieldLengths
{
    /// <summary>Person names: user first/last names and commenter display names.</summary>
    public const int PersonName = 100;

    /// <summary>Email addresses; matches the maximum practical RFC 5321 path length.</summary>
    public const int Email = 320;

    /// <summary>Absolute or relative URLs such as a commenter's website or a redirect path.</summary>
    public const int Url = 300;

    /// <summary>Post and page titles.</summary>
    public const int PostTitle = 200;

    /// <summary>Post and page URL slugs.</summary>
    public const int Slug = 200;

    /// <summary>Post summary shown on list pages and used as the default meta description.</summary>
    public const int PostSummary = 500;

    /// <summary>SEO title override; search engines truncate at roughly this length.</summary>
    public const int MetaTitle = 70;

    /// <summary>SEO meta description override; search engines truncate at roughly this length.</summary>
    public const int MetaDescription = 160;

    /// <summary>Tag display name as entered by the author.</summary>
    public const int TagName = 50;

    /// <summary>Tag URL slug; longer than <see cref="TagName"/> because symbols expand (for example <c>#</c> becomes <c>sharp</c>).</summary>
    public const int TagSlug = 60;

    /// <summary>Optional tag description shown on the tag page.</summary>
    public const int TagDescription = 500;

    /// <summary>Comment body as submitted (Markdown source).</summary>
    public const int CommentBody = 4000;

    /// <summary>Slugified media file name used in public media URLs.</summary>
    public const int MediaFileName = 200;

    /// <summary>Image alternative text.</summary>
    public const int AltText = 300;

    /// <summary>Image caption.</summary>
    public const int Caption = 500;

    /// <summary>Storage key (path) of a media object inside <c>IMediaStorage</c>.</summary>
    public const int StorageKey = 400;

    /// <summary>Short random media identifier used in public media URLs (fixed length).</summary>
    public const int MediaPublicId = 12;

    /// <summary>Media type detected by decoding an upload, such as <c>image/jpeg</c>.</summary>
    public const int ContentType = 50;

    /// <summary>Rendition encoding, such as <c>webp</c> or <c>jpeg</c>.</summary>
    public const int RenditionFormat = 10;

    /// <summary>Lowercase hex SHA-256 digest (fixed length): media content hashes and commenter IP hashes.</summary>
    public const int Sha256Hex = 64;

    /// <summary>Commenter user agent kept for moderation context.</summary>
    public const int UserAgent = 300;

    /// <summary>Human-readable reasons recorded by the spam guard.</summary>
    public const int SpamReasons = 500;

    /// <summary>Value of a comment block entry; long enough for a full email address.</summary>
    public const int CommentBlockValue = Email;

    /// <summary>Free-text note on a comment block entry.</summary>
    public const int CommentBlockNote = 500;

    /// <summary>URL-safe preview token; 256 bits encode to 43 base64url characters.</summary>
    public const int PreviewToken = 64;

    /// <summary>Site title shown in the header and browser tab.</summary>
    public const int SiteTitle = 100;

    /// <summary>Site tagline shown under the title.</summary>
    public const int Tagline = 200;

    /// <summary>Author biography (Markdown source).</summary>
    public const int AuthorBio = 2000;

    /// <summary>Name of a social network in the site's social links, such as <c>GitHub</c>.</summary>
    public const int SocialNetwork = 50;

    /// <summary>IANA time zone identifier, such as <c>America/Chicago</c>.</summary>
    public const int TimeZoneId = 64;

    /// <summary>.NET custom date format string used for displayed dates.</summary>
    public const int DateFormat = 50;

    /// <summary>Extra lines appended to the generated <c>robots.txt</c>.</summary>
    public const int RobotsTxtExtras = 2000;
}