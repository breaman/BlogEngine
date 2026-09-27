using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a reader's <see cref="CommentSubmission"/> (design 6.5, 8.1): name, email, optional website and body.
/// </summary>
/// <remarks>
/// Only well-formedness is checked here. Whether a comment is spam is the spam guard's job, and it never tells the
/// commenter; a validation message is shown to everyone, bots included.
/// </remarks>
public sealed class CommentSubmissionValidator : AbstractValidator<CommentSubmission>
{
    /// <summary>Defines the comment rules.</summary>
    public CommentSubmissionValidator()
    {
        RuleFor(c => c.AuthorName)
            .NotEmpty()
            .MaximumLength(FieldLengths.PersonName)
            .WithName("Name");

        RuleFor(c => c.AuthorEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(FieldLengths.Email)
            .WithName("Email");

        RuleFor(c => c.AuthorUrl)
            .MaximumLength(FieldLengths.Url)
            .Must(HttpUrls.IsBlankOrAbsoluteHttp)
            .WithMessage("'{PropertyName}' must be a full web address, such as https://example.com.")
            .WithName("Website");

        RuleFor(c => c.Body)
            .NotEmpty()
            .MaximumLength(FieldLengths.CommentBody)
            .WithName("Comment");
    }
}