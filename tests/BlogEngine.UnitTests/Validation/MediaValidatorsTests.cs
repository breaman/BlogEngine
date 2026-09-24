using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Validation;

namespace BlogEngine.UnitTests.Validation;

/// <summary>
/// Tests the shared media validators (T2.5, T2.10): <see cref="MediaUpdateValidator"/> lengths and
/// <see cref="MediaEditOperationsValidator"/> rotations and sizes.
/// </summary>
public class MediaValidatorsTests
{
    private static readonly MediaUpdateValidator UpdateValidator = new();
    private static readonly MediaEditOperationsValidator EditValidator = new();

    /// <summary>Empty alt text is allowed (decorative images); lengths come from <see cref="FieldLengths"/>.</summary>
    [Test]
    public async Task Update_EnforcesLengths()
    {
        var ok = UpdateValidator.Validate(new MediaUpdateRequest { AltText = "", Caption = new string('c', FieldLengths.Caption) });
        var tooLong = UpdateValidator.Validate(new MediaUpdateRequest
        {
            AltText = new string('a', FieldLengths.AltText + 1),
            Caption = new string('c', FieldLengths.Caption + 1)
        });

        await Assert.That(ok.IsValid).IsTrue();
        await Assert.That(tooLong.ToDictionary().Keys).IsEquivalentTo([nameof(MediaUpdateRequest.AltText), nameof(MediaUpdateRequest.Caption)]);
    }

    /// <summary>Only quarter turns are allowed (Q8).</summary>
    [Test]
    [Arguments(0, true)]
    [Arguments(90, true)]
    [Arguments(180, true)]
    [Arguments(270, true)]
    [Arguments(45, false)]
    [Arguments(360, false)]
    [Arguments(-90, false)]
    public async Task Edit_Rotation(int rotate, bool valid)
    {
        await Assert.That(EditValidator.Validate(new MediaEditOperations { Rotate = rotate }).IsValid).IsEqualTo(valid);
    }

    /// <summary>Crop and resize need positive sizes and a crop can't start before the image.</summary>
    [Test]
    public async Task Edit_RejectsBadCropAndResize()
    {
        var result = EditValidator.Validate(new MediaEditOperations
        {
            Crop = new MediaCropRect { X = -1, Y = 0, Width = 0, Height = 10 },
            Resize = new MediaSize { Width = 10, Height = 0 }
        });

        await Assert.That(result.ToDictionary().Keys).IsEquivalentTo(["Crop.X", "Crop.Width", "Resize.Height"]);
    }
}
