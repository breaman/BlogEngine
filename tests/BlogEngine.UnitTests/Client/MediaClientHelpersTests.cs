using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;

using Microsoft.Extensions.Logging.Abstractions;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests the small client helpers behind the media pages (T2.8–T2.11): the preview's lookup cache, the cropper's
/// operations, and the delete warning text.
/// </summary>
public class MediaClientHelpersTests
{
    /// <summary>Each id is requested once, including ids the library doesn't have.</summary>
    [Test]
    public async Task LookupCache_AsksOncePerId()
    {
        var media = new FakeMediaService(new MediaLookupItem(1, "aaaaaaaaaaaa", "a.jpg", 10, 10, 1, ""));
        var cache = new MediaLookupCache(media, NullLogger<MediaLookupCache>.Instance);

        await cache.LoadAsync(["aaaaaaaaaaaa", "zzzzzzzzzzzz"]);
        var again = await cache.LoadAsync(["aaaaaaaaaaaa", "zzzzzzzzzzzz"]);

        await Assert.That(media.Lookups).Count().IsEqualTo(1);
        await Assert.That(again).IsFalse();
        await Assert.That(cache.Find("aaaaaaaaaaaa")!.FileName).IsEqualTo("a.jpg");
        await Assert.That(cache.Find("zzzzzzzzzzzz")).IsNull();
        await Assert.That(cache.HasAll(["aaaaaaaaaaaa", "zzzzzzzzzzzz"])).IsTrue();
    }

    /// <summary>An item the page already has (inserted, edited) is known without a request.</summary>
    [Test]
    public async Task LookupCache_RememberAddsItem()
    {
        var media = new FakeMediaService();
        var cache = new MediaLookupCache(media, NullLogger<MediaLookupCache>.Instance);

        cache.Remember(new MediaItemDto { Id = 2, PublicId = "bbbbbbbbbbbb", FileName = "b.png", Width = 5, Height = 6, Version = 4 });
        await cache.LoadAsync(["bbbbbbbbbbbb"]);

        await Assert.That(media.Lookups).IsEmpty();
        await Assert.That(cache.Find("bbbbbbbbbbbb")!.Version).IsEqualTo(4);
    }

    /// <summary>A selection covering the whole image and a resize to the crop size are not sent as operations.</summary>
    [Test]
    public async Task CropperState_OmitsNoOpCropAndResize()
    {
        var state = new ImageCropperState
        {
            OriginalWidth = 400,
            OriginalHeight = 300,
            Rotate = 90,
            Crop = new MediaCropRect { X = 0, Y = 0, Width = 300, Height = 400 }
        };

        var operations = state.ToOperations(new MediaSize { Width = 300, Height = 400 });

        await Assert.That(operations.Rotate).IsEqualTo(90);
        await Assert.That(operations.Crop).IsNull();
        await Assert.That(operations.Resize).IsNull();
    }

    /// <summary>A partial selection and a smaller size are sent.</summary>
    [Test]
    public async Task CropperState_KeepsRealCropAndResize()
    {
        var state = new ImageCropperState
        {
            OriginalWidth = 400,
            OriginalHeight = 300,
            Crop = new MediaCropRect { X = 10, Y = 20, Width = 200, Height = 100 }
        };

        var operations = state.ToOperations(new MediaSize { Width = 100, Height = 50 });

        await Assert.That(operations.Crop!.X).IsEqualTo(10);
        await Assert.That(operations.Resize!.Width).IsEqualTo(100);
        await Assert.That(operations.IsIdentity).IsFalse();
    }

    /// <summary>The "in use" warning names the posts, marks trashed ones and shortens long lists.</summary>
    [Test]
    public async Task UsageText_ListsPosts()
    {
        var one = MediaDeletion.UsageText([new MediaUsageDto { PostId = 1, Title = "Holiday" }]);
        var many = MediaDeletion.UsageText([.. Enumerable.Range(1, 7).Select(i => new MediaUsageDto { PostId = i, Title = $"P{i}", IsInTrash = i == 1 })]);

        await Assert.That(one).IsEqualTo("Used in 1 post: \"Holiday\".");
        await Assert.That(many).IsEqualTo("Used in 7 posts: \"P1\" (in trash), \"P2\", \"P3\", \"P4\", \"P5\" and 2 more.");
    }

    /// <summary>Sizes read naturally.</summary>
    [Test]
    [Arguments(512L, "512 bytes")]
    [Arguments(2048L, "2 KB")]
    [Arguments(5_452_595L, "5.2 MB")]
    public async Task FormatSize_ReadsNaturally(long bytes, string expected)
    {
        using var culture = new CultureScope("en-US");

        await Assert.That(MediaFormat.Size(bytes)).IsEqualTo(expected);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name)
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
        }

        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
