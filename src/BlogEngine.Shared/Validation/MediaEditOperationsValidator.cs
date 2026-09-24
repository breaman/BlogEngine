using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Media;

using FluentValidation;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates the shape of <see cref="MediaEditOperations"/> (design 9.2, Q8): quarter-turn rotations and
/// positive sizes. Whether the crop fits the image can only be checked against the original, which the server
/// does with <see cref="MediaGeometry.CheckFits"/>.
/// </summary>
public sealed class MediaEditOperationsValidator : AbstractValidator<MediaEditOperations>
{
    /// <summary>Defines the edit rules.</summary>
    public MediaEditOperationsValidator()
    {
        RuleFor(o => o.Rotate)
            .Must(r => r is 0 or 90 or 180 or 270)
            .WithMessage("Rotation must be 0, 90, 180 or 270 degrees.");

        RuleFor(o => o.Crop!).ChildRules(crop =>
        {
            crop.RuleFor(c => c.X).GreaterThanOrEqualTo(0);
            crop.RuleFor(c => c.Y).GreaterThanOrEqualTo(0);
            crop.RuleFor(c => c.Width).InclusiveBetween(1, MediaGeometry.MaxDimension);
            crop.RuleFor(c => c.Height).InclusiveBetween(1, MediaGeometry.MaxDimension);
        }).When(o => o.Crop is not null);

        RuleFor(o => o.Resize!).ChildRules(size =>
        {
            size.RuleFor(s => s.Width).InclusiveBetween(1, MediaGeometry.MaxDimension);
            size.RuleFor(s => s.Height).InclusiveBetween(1, MediaGeometry.MaxDimension);
        }).When(o => o.Resize is not null);
    }
}
