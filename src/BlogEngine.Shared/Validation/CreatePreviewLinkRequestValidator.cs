using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a <see cref="CreatePreviewLinkRequest"/>: a link lasts at least a day and at most
/// <see cref="CreatePreviewLinkRequest.MaxExpiresInDays"/> days, so a forgotten link doesn't stay open for good.
/// </summary>
public sealed class CreatePreviewLinkRequestValidator : AbstractValidator<CreatePreviewLinkRequest>
{
    /// <summary>Defines the rules.</summary>
    public CreatePreviewLinkRequestValidator()
    {
        RuleFor(r => r.ExpiresInDays)
            .InclusiveBetween(1, CreatePreviewLinkRequest.MaxExpiresInDays)
            .WithName("Expiry")
            .WithMessage("A preview link can last from {From} to {To} days.");
    }
}