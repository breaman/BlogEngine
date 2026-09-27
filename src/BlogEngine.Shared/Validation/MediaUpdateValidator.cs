using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates <see cref="MediaUpdateRequest"/>: alt text and caption lengths (design 6.6).
/// </summary>
public sealed class MediaUpdateValidator : AbstractValidator<MediaUpdateRequest>
{
    /// <summary>Defines the metadata rules.</summary>
    public MediaUpdateValidator()
    {
        RuleFor(m => m.AltText)
            .MaximumLength(FieldLengths.AltText)
            .WithName("Alt text");

        RuleFor(m => m.Caption)
            .MaximumLength(FieldLengths.Caption);
    }
}