using BlogEngine.Server.Services.Media;
using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Tests <see cref="MediaFileNames"/> (design 6.6, T2.3): random 12-character public ids and slugified file names
/// with the detected format's extension.
/// </summary>
public class MediaFileNamesTests
{
    /// <summary>Uploaded names become slugs; the extension comes from the detected format, not the upload.</summary>
    [Test]
    [Arguments("Sunset at Lake.JPG", "jpg", "sunset-at-lake.jpg")]
    [Arguments("IMG_0042.jpeg", "jpg", "img-0042.jpg")]
    [Arguments("photo.jpg", "png", "photo.png")]
    [Arguments("Crème brûlée.webp", "webp", "creme-brulee.webp")]
    [Arguments(@"C:\Users\me\Pictures\cat.gif", "gif", "cat.gif")]
    [Arguments("日本.png", "png", "image.png")]
    [Arguments("", "jpg", "image.jpg")]
    [Arguments(null, "jpg", "image.jpg")]
    public async Task FromUpload_SlugifiesName(string? uploaded, string extension, string expected)
    {
        await Assert.That(MediaFileNames.FromUpload(uploaded, extension)).IsEqualTo(expected);
    }

    /// <summary>Very long names are shortened to fit the column, extension included.</summary>
    [Test]
    public async Task FromUpload_LongName_FitsColumn()
    {
        var name = MediaFileNames.FromUpload(string.Join(' ', Enumerable.Repeat("holiday", 60)) + ".jpg", "jpg");

        await Assert.That(name.Length).IsLessThanOrEqualTo(FieldLengths.MediaFileName);
        await Assert.That(name).EndsWith(".jpg");
    }

    /// <summary>Public ids are 12 lowercase letters or digits and don't repeat.</summary>
    [Test]
    public async Task NewPublicId_IsRandomLowercase()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => MediaFileNames.NewPublicId()).ToList();

        await Assert.That(ids.All(id => id.Length == FieldLengths.MediaPublicId && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))).IsTrue();
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count);
    }
}
