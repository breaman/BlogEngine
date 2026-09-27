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
            .Must(HttpUrls.IsBlankOrAbsoluteHttp).WithMessage("'{PropertyName}' must be an absolute http or https URL.");
    }
}