using System.Globalization;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a <see cref="SiteSettingsDto"/> (design 13) before it is saved.
/// </summary>
/// <remarks>
/// Besides lengths and ranges, it checks that the time zone is known, that the date format can format a date,
/// and that the rendition widths are usable. Each social link is checked by <see cref="SocialLinkValidator"/>.
/// </remarks>
/// <example>
/// <code>
/// await settingsValidator.ValidateAndThrowAsync(settings, cancellationToken);
/// </code>
/// </example>
public sealed class SiteSettingsValidator : AbstractValidator<SiteSettingsDto>
{
    /// <summary>Narrowest rendition width allowed, in pixels.</summary>
    public const int MinRenditionWidth = 16;

    /// <summary>Widest rendition width allowed, in pixels.</summary>
    public const int MaxRenditionWidth = 8192;

    /// <summary>Defines the settings rules.</summary>
    public SiteSettingsValidator()
    {
        // Identity
        RuleFor(s => s.SiteTitle).NotEmpty().MaximumLength(FieldLengths.SiteTitle);
        RuleFor(s => s.Tagline).MaximumLength(FieldLengths.Tagline);
        RuleFor(s => s.Description).MaximumLength(FieldLengths.MetaDescription);
        RuleFor(s => s.AuthorName).MaximumLength(FieldLengths.PersonName);
        RuleFor(s => s.AuthorBioMarkdown).MaximumLength(FieldLengths.AuthorBio);
        RuleForEach(s => s.SocialLinks).SetValidator(new SocialLinkValidator());

        // Reading
        RuleFor(s => s.PostsPerPage).InclusiveBetween(1, 100);

        // Localization
        RuleFor(s => s.TimeZoneId)
            .NotEmpty()
            .MaximumLength(FieldLengths.TimeZoneId)
            .Must(BeKnownTimeZone).WithMessage("'{PropertyValue}' is not a known time zone.");

        RuleFor(s => s.DateFormat)
            .NotEmpty()
            .MaximumLength(FieldLengths.DateFormat)
            .Must(BeUsableDateFormat).WithMessage("'{PropertyValue}' is not a valid date format.");

        // Comments
        RuleFor(s => s.CloseCommentsAfterDays).InclusiveBetween(0, 3650);
        RuleFor(s => s.MaxCommentLinks).InclusiveBetween(0, 50);

        // Media
        RuleFor(s => s.MaxUploadSizeMegabytes).InclusiveBetween(1, 500);
        RuleFor(s => s.DownscaleOriginalsAbovePixels).InclusiveBetween(0, 20000);
        RuleFor(s => s.RenditionWidths)
            .Must(w => w is { Count: > 0 } && w.TrueForAll(width => width is >= MinRenditionWidth and <= MaxRenditionWidth))
            .WithMessage($"Rendition widths must contain at least one width between {MinRenditionWidth} and {MaxRenditionWidth} pixels.");

        // SEO
        RuleFor(s => s.RobotsTxtExtras).MaximumLength(FieldLengths.RobotsTxtExtras);
    }

    /// <summary>Whether the id names a time zone this machine knows (IANA or Windows).</summary>
    private static bool BeKnownTimeZone(string? timeZoneId)
    {
        // Blank values are reported by NotEmpty.
        return string.IsNullOrWhiteSpace(timeZoneId) || TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
    }

    /// <summary>Checks that the format string formats a date without throwing.</summary>
    private static bool BeUsableDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

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
}