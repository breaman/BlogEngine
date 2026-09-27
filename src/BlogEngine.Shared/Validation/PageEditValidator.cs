using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a <see cref="PageEditDto"/> the same way in the page editor (WebAssembly) and on the server (design 6.7,
/// A17).
/// </summary>
/// <remarks>
/// Besides the lengths and the slug format shared with posts, a typed slug must not be one of the
/// <see cref="ReservedSlugs"/>, since <c>/{slug}</c> would clash with a route of the site. A slug generated from the title
/// is kept clear of them by the server instead.
/// </remarks>
public sealed class PageEditValidator : AbstractValidator<PageEditDto>
{
    /// <summary>Highest navigation position; keeps the value meaningful and the column small.</summary>
    public const int MaxNavOrder = 999;

    /// <summary>Defines the page rules.</summary>
    public PageEditValidator()
    {
        RuleFor(p => p.Title)
            .NotEmpty().WithMessage("A title is required.")
            .MaximumLength(FieldLengths.PostTitle).WithMessage("The title can be at most {MaxLength} characters.");

        RuleFor(p => p.Slug)
            .MaximumLength(FieldLengths.Slug).WithMessage("The slug can be at most {MaxLength} characters.")
            .Matches(ValidationPatterns.Slug)
            .WithMessage("The slug may contain only lowercase letters, digits and single hyphens between words.")
            .Must(slug => !ReservedSlugs.IsReserved(slug))
            .WithMessage("'{PropertyValue}' is used by the site itself. Choose another slug.")
            // Blank means "generate it from the title", so only a typed slug is checked.
            .When(p => !string.IsNullOrEmpty(p.Slug), ApplyConditionTo.AllValidators);

        RuleFor(p => p.Summary)
            .MaximumLength(FieldLengths.PostSummary).WithMessage("The summary can be at most {MaxLength} characters.");

        RuleFor(p => p.MetaTitle)
            .MaximumLength(FieldLengths.MetaTitle).WithMessage("The meta title can be at most {MaxLength} characters.");

        RuleFor(p => p.MetaDescription)
            .MaximumLength(FieldLengths.MetaDescription).WithMessage("The meta description can be at most {MaxLength} characters.");

        RuleFor(p => p.NavOrder)
            .InclusiveBetween(0, MaxNavOrder).WithName("Navigation order")
            .WithMessage("{PropertyName} must be between {From} and {To}.");
    }
}