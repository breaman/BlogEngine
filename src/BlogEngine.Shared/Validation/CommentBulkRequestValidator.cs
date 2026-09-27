using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a bulk moderation request (<see cref="CommentBulkRequest"/>, design 8.4): a known action and between one
/// and <see cref="CommentBulkRequest.MaxIds"/> comments.
/// </summary>
public sealed class CommentBulkRequestValidator : AbstractValidator<CommentBulkRequest>
{
    /// <summary>Defines the bulk request rules.</summary>
    public CommentBulkRequestValidator()
    {
        RuleFor(r => r.Action).IsInEnum();

        RuleFor(r => r.Ids)
            .NotEmpty().WithMessage("Select at least one comment.")
            .Must(ids => ids.Count <= CommentBulkRequest.MaxIds)
            .WithMessage($"Select at most {CommentBulkRequest.MaxIds} comments at a time.")
            .WithName("Comments");
    }
}