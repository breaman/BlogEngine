using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Media;

namespace BlogEngine.UnitTests.Media;

/// <summary>
/// Tests <see cref="MediaGeometry"/> (design 9.2, T2.9, T2.10): rotated sizes, mapping a selection drawn over a
/// scaled display to natural pixels, resizing without upscaling, and checking operations against an original.
/// </summary>
public class MediaGeometryTests
{
    /// <summary>Quarter turns swap width and height; half turns don't.</summary>
    [Test]
    [Arguments(0, 4000, 3000)]
    [Arguments(90, 3000, 4000)]
    [Arguments(180, 4000, 3000)]
    [Arguments(270, 3000, 4000)]
    [Arguments(-90, 3000, 4000)]
    [Arguments(450, 3000, 4000)]
    public async Task RotatedSize_SwapsOnQuarterTurns(int rotate, int width, int height)
    {
        await Assert.That(MediaGeometry.RotatedSize(4000, 3000, rotate)).IsEqualTo((width, height));
    }

    /// <summary>Any angle becomes 0, 90, 180 or 270.</summary>
    [Test]
    [Arguments(0, 0)]
    [Arguments(360, 0)]
    [Arguments(-90, 270)]
    [Arguments(-180, 180)]
    [Arguments(630, 270)]
    [Arguments(100, 90)]
    public async Task NormalizeRotation_WrapsToQuarterTurns(int degrees, int expected)
    {
        await Assert.That(MediaGeometry.NormalizeRotation(degrees)).IsEqualTo(expected);
    }

    /// <summary>An image shown at a quarter of its size, offset inside the canvas, maps back to natural pixels.</summary>
    [Test]
    public async Task ToNatural_ScalesAndOffsets()
    {
        var image = new DisplayRect(50, 25, 1000, 750);
        var selection = new DisplayRect(150, 75, 500, 250);

        var crop = MediaGeometry.ToNatural(selection, image, 4000, 3000);

        await Assert.That((crop.X, crop.Y, crop.Width, crop.Height)).IsEqualTo((400, 200, 2000, 1000));
    }

    /// <summary>
    /// After a quarter turn the cropper shows the rotated original, so the same on-screen selection maps into the
    /// rotated image's natural coordinates (T2.9's done-when): a 4000 × 3000 photo becomes 3000 × 4000.
    /// </summary>
    [Test]
    public async Task ToNatural_AfterRotation_UsesRotatedDimensions()
    {
        var (width, height) = MediaGeometry.RotatedSize(4000, 3000, 90);
        var image = new DisplayRect(0, 0, 600, 800);
        var bottomHalf = new DisplayRect(0, 400, 600, 400);

        var crop = MediaGeometry.ToNatural(bottomHalf, image, width, height);

        await Assert.That((crop.X, crop.Y, crop.Width, crop.Height)).IsEqualTo((0, 2000, 3000, 2000));
        await Assert.That(MediaGeometry.CheckFits(new MediaEditOperations { Rotate = 90, Crop = crop }, 4000, 3000)).IsNull();
    }

    /// <summary>The full selection maps to exactly the full image, whatever the rounding.</summary>
    [Test]
    public async Task ToNatural_FullSelection_IsWholeImage()
    {
        var image = new DisplayRect(12.3, 7.7, 333.3, 222.2);

        var crop = MediaGeometry.ToNatural(image, image, 1999, 1333);

        await Assert.That((crop.X, crop.Y, crop.Width, crop.Height)).IsEqualTo((0, 0, 1999, 1333));
    }

    /// <summary>A selection hanging over the edges is clamped to the image and never empty.</summary>
    [Test]
    public async Task ToNatural_ClampsToImage()
    {
        var image = new DisplayRect(0, 0, 100, 100);

        var outside = MediaGeometry.ToNatural(new DisplayRect(-20, 90, 200, 50), image, 1000, 1000);
        var tiny = MediaGeometry.ToNatural(new DisplayRect(50, 50, 0.01, 0.01), image, 1000, 1000);

        await Assert.That((outside.X, outside.Y, outside.Width, outside.Height)).IsEqualTo((0, 900, 1000, 100));
        await Assert.That((tiny.Width, tiny.Height)).IsEqualTo((1, 1));
    }

    /// <summary>Resizing keeps the aspect ratio and never goes above the bounds.</summary>
    [Test]
    public async Task ScaleToWidthAndHeight_KeepRatioWithoutUpscaling()
    {
        var bounds = new MediaSize { Width = 1200, Height = 800 };

        var byWidth = MediaGeometry.ScaleToWidth(bounds, 600);
        var byHeight = MediaGeometry.ScaleToHeight(bounds, 200);
        var tooWide = MediaGeometry.ScaleToWidth(bounds, 5000);
        var zero = MediaGeometry.ScaleToHeight(bounds, 0);

        await Assert.That((byWidth.Width, byWidth.Height)).IsEqualTo((600, 400));
        await Assert.That((byHeight.Width, byHeight.Height)).IsEqualTo((300, 200));
        await Assert.That((tooWide.Width, tooWide.Height)).IsEqualTo((1200, 800));
        await Assert.That((zero.Width, zero.Height)).IsEqualTo((2, 1));
    }

    /// <summary>Operations are checked against the rotated original and the cropped size.</summary>
    [Test]
    public async Task CheckFits_ReportsCropAndResizeProblems()
    {
        var cropTooTall = new MediaEditOperations { Crop = new MediaCropRect { X = 0, Y = 0, Width = 100, Height = 301 } };
        var cropFitsRotated = new MediaEditOperations { Rotate = 270, Crop = new MediaCropRect { X = 0, Y = 0, Width = 100, Height = 301 } };
        var upscale = new MediaEditOperations { Crop = new MediaCropRect { X = 0, Y = 0, Width = 100, Height = 100 }, Resize = new MediaSize { Width = 101, Height = 100 } };

        await Assert.That(MediaGeometry.CheckFits(cropTooTall, 400, 300)).IsNotNull();
        await Assert.That(MediaGeometry.CheckFits(cropFitsRotated, 400, 300)).IsNull();
        await Assert.That(MediaGeometry.CheckFits(upscale, 400, 300)).Contains("100 × 100");
    }

    /// <summary>Only no rotation, no flips, no crop and no resize counts as unedited.</summary>
    [Test]
    public async Task IsIdentity_OnlyForNoOperations()
    {
        await Assert.That(new MediaEditOperations().IsIdentity).IsTrue();
        await Assert.That(new MediaEditOperations { Rotate = 360 }.IsIdentity).IsTrue();
        await Assert.That(new MediaEditOperations { FlipVertical = true }.IsIdentity).IsFalse();
        await Assert.That(new MediaEditOperations { Resize = new MediaSize { Width = 1, Height = 1 } }.IsIdentity).IsFalse();
    }

    /// <summary>A locked ratio is met as closely as whole pixels allow, around the same center, inside the image.</summary>
    [Test]
    public async Task FitAspectRatio_SnapsToExactRatio()
    {
        var square = MediaGeometry.FitAspectRatio(new MediaCropRect { X = 0, Y = 301, Width = 1000, Height = 1002 }, 1, 1000, 1600);
        var wide = MediaGeometry.FitAspectRatio(new MediaCropRect { X = 100, Y = 100, Width = 1601, Height = 899 }, 16 / 9.0, 2000, 1000);
        var tooTall = MediaGeometry.FitAspectRatio(new MediaCropRect { X = 0, Y = 0, Width = 1000, Height = 500 }, 0.5, 1000, 500);

        await Assert.That((square.X, square.Y, square.Width, square.Height)).IsEqualTo((0, 302, 1000, 1000));
        await Assert.That((wide.Width, wide.Height)).IsEqualTo((1601, 901));
        await Assert.That((tooTall.Width, tooTall.Height)).IsEqualTo((250, 500));
        await Assert.That(tooTall.X + tooTall.Width).IsLessThanOrEqualTo(1000);
    }

    /// <summary>No ratio (free selection) leaves the crop alone.</summary>
    [Test]
    public async Task FitAspectRatio_NoRatio_IsUnchanged()
    {
        var crop = new MediaCropRect { X = 1, Y = 2, Width = 3, Height = 4 };

        await Assert.That(MediaGeometry.FitAspectRatio(crop, 0, 10, 10)).IsSameReferenceAs(crop);
    }
}
