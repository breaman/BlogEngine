using BlogEngine.Client.Services;

namespace BlogEngine.UnitTests.Client;

/// <summary>Tests <see cref="RenditionWidthsText"/>: the rendition widths text box on the settings page (T4.25).</summary>
public class RenditionWidthsTextTests
{
    /// <summary>Widths may be separated by commas, semicolons or spaces, carry "px", and come back sorted without duplicates.</summary>
    [Test]
    [Arguments("320, 640, 960", new[] { 320, 640, 960 })]
    [Arguments("960;320 640px", new[] { 320, 640, 960 })]
    [Arguments(" 640 ,640, 320PX ", new[] { 320, 640 })]
    [Arguments("", new int[0])]
    public async Task TryParse_ReadsWidths(string text, int[] expected)
    {
        var parsed = RenditionWidthsText.TryParse(text, out var widths, out var error);

        await Assert.That(parsed).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(widths).IsEquivalentTo(expected);
    }

    /// <summary>Anything that isn't a whole number of pixels is reported by name.</summary>
    [Test]
    [Arguments("320, wide", "'wide'")]
    [Arguments("320, -640", "'-640'")]
    [Arguments("320.5", "'320.5'")]
    public async Task TryParse_RejectsNonNumbers(string text, string quoted)
    {
        var parsed = RenditionWidthsText.TryParse(text, out var widths, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(widths).IsEmpty();
        await Assert.That(error).Contains(quoted);
    }

    /// <summary>Formatting is the comma list the parser reads back.</summary>
    [Test]
    public async Task Format_RoundTrips()
    {
        var text = RenditionWidthsText.Format([320, 640, 1280]);
        RenditionWidthsText.TryParse(text, out var widths, out _);

        await Assert.That(text).IsEqualTo("320, 640, 1280");
        await Assert.That(widths).IsEquivalentTo([320, 640, 1280]);
    }
}
