using System.Text.RegularExpressions;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a new blocklist entry (<see cref="CommentBlockRequest"/>, design 6.5, T3.9). The value is checked in the
/// normalized form it will be stored in (<see cref="CommentBlockValues.Normalize"/>), so <c>@Example.com</c> passes as
/// a domain.
/// </summary>
public sealed class CommentBlockRequestValidator : AbstractValidator<CommentBlockRequest>
{
    /// <summary>Shortest keyword; a one-letter keyword would match nearly every comment.</summary>
    public const int MinKeywordLength = 2;

    /// <summary>Defines the blocklist rules.</summary>
    public CommentBlockRequestValidator()
    {
        RuleFor(b => b.Kind).IsInEnum();

        RuleFor(b => b.Value)
            .NotEmpty()
            .MaximumLength(FieldLengths.CommentBlockValue)
            .WithName("Value");

        RuleFor(b => b.Value)
            .Must(v => CommentBlockValues.Normalize(CommentBlockKind.Keyword, v).Length >= MinKeywordLength)
            .WithMessage($"A keyword must be at least {MinKeywordLength} characters long.")
            .When(b => b.Kind == CommentBlockKind.Keyword && !string.IsNullOrWhiteSpace(b.Value));

        RuleFor(b => b.Value)
            .EmailAddress()
            .WithMessage("Enter an email address, such as spammer@example.com.")
            .When(b => b.Kind == CommentBlockKind.Email && !string.IsNullOrWhiteSpace(b.Value));

        RuleFor(b => b.Value)
            .Must(v => Regex.IsMatch(CommentBlockValues.Normalize(CommentBlockKind.Domain, v), ValidationPatterns.Domain))
            .WithMessage("Enter a domain, such as example.com.")
            .When(b => b.Kind == CommentBlockKind.Domain && !string.IsNullOrWhiteSpace(b.Value));

        RuleFor(b => b.Value)
            .Must(v => Regex.IsMatch(CommentBlockValues.Normalize(CommentBlockKind.IpHash, v), ValidationPatterns.Sha256Hex))
            .WithMessage("An IP hash is 64 hexadecimal characters.")
            .When(b => b.Kind == CommentBlockKind.IpHash && !string.IsNullOrWhiteSpace(b.Value));

        RuleFor(b => b.Note).MaximumLength(FieldLengths.CommentBlockNote);
    }
}