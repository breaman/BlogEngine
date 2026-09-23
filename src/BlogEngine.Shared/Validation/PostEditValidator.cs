using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Text;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a <see cref="PostEditDto"/> the same way in the editor (WebAssembly) and on the server.
/// </summary>
/// <remarks>
/// Covers the title (required), field lengths, the slug format and the tag rules from
/// <see cref="TagNormalizer"/>. The server always re-validates; the client runs it only to show errors early.
/// Every tag error is reported under <see cref="PostEditDto.Tags"/> rather than per index, so the editor can
/// show them next to the single tag input.
/// </remarks>
/// <example>
/// <code>
/// var errors = postValidator.Validate(post).ToDictionary();
/// if (errors.Count &gt; 0)
/// {
///     return new PostInvalid(errors.AsReadOnly());
/// }
/// </code>
/// </example>
public sealed class PostEditValidator : AbstractValidator<PostEditDto>
{
    /// <summary>Most tags a single post may have; keeps the tag list meaningful and the save bounded.</summary>
    public const int MaxTags = 20;

    /// <summary>Defines the post rules.</summary>
    public PostEditValidator()
    {
        RuleFor(p => p.Title)
            .NotEmpty().WithMessage("A title is required.")
            .MaximumLength(FieldLengths.PostTitle).WithMessage("The title can be at most {MaxLength} characters.");

        RuleFor(p => p.Slug)
            .MaximumLength(FieldLengths.Slug).WithMessage("The slug can be at most {MaxLength} characters.")
            .Matches(ValidationPatterns.Slug)
            .WithMessage("The slug may contain only lowercase letters, digits and single hyphens between words.")
            // Blank means "generate it from the title", so only a typed slug has to match the pattern.
            .When(p => !string.IsNullOrEmpty(p.Slug), ApplyConditionTo.CurrentValidator);

        RuleFor(p => p.Summary)
            .MaximumLength(FieldLengths.PostSummary).WithMessage("The summary can be at most {MaxLength} characters.");

        RuleFor(p => p.MetaTitle)
            .MaximumLength(FieldLengths.MetaTitle).WithMessage("The meta title can be at most {MaxLength} characters.");

        RuleFor(p => p.MetaDescription)
            .MaximumLength(FieldLengths.MetaDescription).WithMessage("The meta description can be at most {MaxLength} characters.");

        RuleFor(p => p.Tags).Custom(ValidateTags);
    }

    /// <summary>Checks every tag name and the number of distinct tags.</summary>
    private static void ValidateTags(List<string>? tags, ValidationContext<PostEditDto> context)
    {
        if (tags is null)
        {
            return;
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var result = TagNormalizer.Normalize(tag);
            if (!result.IsValid)
            {
                context.AddFailure(nameof(PostEditDto.Tags),
                    string.IsNullOrEmpty(result.Name) ? result.Error! : $"'{result.Name}': {result.Error}");
                continue;
            }

            distinct.Add(result.NormalizedName);
        }

        if (distinct.Count > MaxTags)
        {
            context.AddFailure(nameof(PostEditDto.Tags), $"A post can have at most {MaxTags} tags.");
        }
    }
}
