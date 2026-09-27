using System.Text.RegularExpressions;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Text;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates an <see cref="UpdateTagRequest"/> (design 6.4, O3): the name follows the same rules as a name typed in the
/// editor (<see cref="TagNormalizer"/>), a typed slug has the slug shape, and the description fits its column.
/// </summary>
/// <remarks>
/// Whether the name or slug is free is checked by the server, which knows the other tags. Surrounding spaces are
/// ignored, as the server trims them.
/// </remarks>
/// <example>
/// <code>
/// var result = await validator.ValidateAsync(new UpdateTagRequest { Name = "C#", Slug = "csharp" });
/// </code>
/// </example>
public sealed partial class UpdateTagRequestValidator : AbstractValidator<UpdateTagRequest>
{
    /// <summary>Defines the rules.</summary>
    public UpdateTagRequestValidator()
    {
        RuleFor(r => r.Name).Custom((name, context) =>
        {
            if (TagNormalizer.Normalize(name).Error is { } error)
            {
                context.AddFailure(error);
            }
        });

        RuleFor(r => r.Slug)
            .Must(slug => slug!.Trim().Length <= FieldLengths.TagSlug)
            .WithMessage($"The slug can be at most {FieldLengths.TagSlug} characters.")
            .Must(slug => SlugRegex().IsMatch(slug!.Trim()))
            .WithMessage("The slug may contain only lowercase letters, digits and single hyphens between words.")
            // Blank means "derive it from the name", so only a typed slug is checked.
            .When(r => !string.IsNullOrWhiteSpace(r.Slug), ApplyConditionTo.AllValidators);

        RuleFor(r => r.Description)
            .MaximumLength(FieldLengths.TagDescription).WithMessage("The description can be at most {MaxLength} characters.");
    }

    [GeneratedRegex(ValidationPatterns.Slug)]
    private static partial Regex SlugRegex();
}