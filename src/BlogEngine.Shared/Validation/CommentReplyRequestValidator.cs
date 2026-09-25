using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates the author's reply (<see cref="CommentReplyRequest"/>, design 8.4): a body within the comment length.
/// </summary>
public sealed class CommentReplyRequestValidator : AbstractValidator<CommentReplyRequest>
{
    /// <summary>Defines the reply rules.</summary>
    public CommentReplyRequestValidator()
    {
        RuleFor(r => r.Body)
            .NotEmpty()
            .MaximumLength(FieldLengths.CommentBody)
            .WithName("Reply");
    }
}
