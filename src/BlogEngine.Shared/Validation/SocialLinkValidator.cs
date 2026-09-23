using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates one <see cref="SocialLinkDto"/>. <see cref="SiteSettingsValidator"/> runs it for each link.
/// </summary>
public sealed class SocialLinkValidator : AbstractValidator<SocialLinkDto>
{
    /// <summary>Defines the social link rules.</summary>
    public SocialLinkValidator()
    {
        RuleFor(l => l.Network)
            .NotEmpty()
            .MaximumLength(FieldLengths.SocialNetwork);

        RuleFor(l => l.Url)
            .NotEmpty()
            .MaximumLength(FieldLengths.Url)
            .Must(BeAbsoluteHttpUrl).WithMessage("'{PropertyName}' must be an absolute http or https URL.");
    }

    /// <summary>Whether the value is an absolute URL a browser can open from the footer.</summary>
    private static bool BeAbsoluteHttpUrl(string? url)
    {
        // Blank values are reported by NotEmpty; don't add a second message for them.
        return string.IsNullOrEmpty(url)
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
    }
}
