using System.Text;

using BlogEngine.Server.Storage;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Tests <see cref="FileSystemMediaStorage"/> (design 9.3, T2.1) against a temporary folder: save, read, overwrite,
/// delete and prefix delete, and keys that try to escape the root.
/// </summary>
public sealed class FileSystemMediaStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "blogengine-media-tests", Guid.NewGuid().ToString("N"));
    private readonly FileSystemMediaStorage storage;

    public FileSystemMediaStorageTests()
    {
        storage = new FileSystemMediaStorage(root);
    }

    /// <summary>A saved object reads back byte for byte, in nested folders.</summary>
    [Test]
    public async Task Save_ThenRead_ReturnsContent()
    {
        await SaveAsync("abc123def456/v2/current.jpg", "pixels");

        await Assert.That(await ReadAsync("abc123def456/v2/current.jpg")).IsEqualTo("pixels");
        await Assert.That(File.Exists(Path.Combine(root, "abc123def456", "v2", "current.jpg"))).IsTrue();
    }

    /// <summary>Saving again replaces the object and leaves no temporary files.</summary>
    [Test]
    public async Task Save_Twice_Overwrites()
    {
        await SaveAsync("item/original.png", "first");
        await SaveAsync("item/original.png", "second");

        await Assert.That(await ReadAsync("item/original.png")).IsEqualTo("second");
        await Assert.That(Directory.GetFiles(Path.Combine(root, "item"))).Count().IsEqualTo(1);
    }

    /// <summary>A missing object reads as null rather than throwing.</summary>
    [Test]
    public async Task Read_Missing_ReturnsNull()
    {
        await Assert.That(await storage.OpenReadAsync("nothing/here.jpg", CancellationToken.None)).IsNull();
    }

    /// <summary>Deleting removes the object and its now-empty folders; deleting again is fine.</summary>
    [Test]
    public async Task Delete_RemovesObjectAndEmptyFolders()
    {
        await SaveAsync("gone/v3/current.gif", "x");

        await storage.DeleteAsync("gone/v3/current.gif", CancellationToken.None);
        await storage.DeleteAsync("gone/v3/current.gif", CancellationToken.None);

        await Assert.That(await storage.OpenReadAsync("gone/v3/current.gif", CancellationToken.None)).IsNull();
        await Assert.That(Directory.Exists(Path.Combine(root, "gone"))).IsFalse();
        await Assert.That(Directory.Exists(root)).IsTrue();
    }

    /// <summary>A folder prefix removes every version of one item and nothing else.</summary>
    [Test]
    public async Task DeletePrefix_RemovesOnlyThatItem()
    {
        await SaveAsync("aaaaaaaaaaaa/original.jpg", "a");
        await SaveAsync("aaaaaaaaaaaa/v2/current.jpg", "a2");
        await SaveAsync("aaaaaaaaaaab/original.jpg", "b");

        await storage.DeletePrefixAsync(MediaStorageKeys.ItemPrefix("aaaaaaaaaaaa"), CancellationToken.None);

        await Assert.That(await storage.OpenReadAsync("aaaaaaaaaaaa/original.jpg", CancellationToken.None)).IsNull();
        await Assert.That(await storage.OpenReadAsync("aaaaaaaaaaaa/v2/current.jpg", CancellationToken.None)).IsNull();
        await Assert.That(await ReadAsync("aaaaaaaaaaab/original.jpg")).IsEqualTo("b");
    }

    /// <summary>A name prefix (no trailing slash) matches files and folders starting with it.</summary>
    [Test]
    public async Task DeletePrefix_NamePrefix_MatchesSiblings()
    {
        await SaveAsync("item/v1/current.jpg", "1");
        await SaveAsync("item/v2/current.jpg", "2");
        await SaveAsync("item/original.jpg", "o");

        await storage.DeletePrefixAsync("item/v", CancellationToken.None);

        await Assert.That(Directory.Exists(Path.Combine(root, "item", "v1"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(root, "item", "v2"))).IsFalse();
        await Assert.That(await ReadAsync("item/original.jpg")).IsEqualTo("o");
    }

    /// <summary>Keys can't reach outside the root.</summary>
    [Test]
    [Arguments("../escape.txt")]
    [Arguments("item/../../escape.txt")]
    [Arguments("/etc/passwd")]
    [Arguments("item\\..\\escape.txt")]
    [Arguments("item//double.txt")]
    [Arguments("./item.txt")]
    public async Task UnsafeKeys_AreRejected(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(key, "x"));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key, CancellationToken.None));
    }

    /// <summary>The key helpers produce the documented layout.</summary>
    [Test]
    public async Task Keys_FollowDesignLayout()
    {
        await Assert.That(MediaStorageKeys.Original("ab12cd34ef56", "jpg")).IsEqualTo("ab12cd34ef56/original.jpg");
        await Assert.That(MediaStorageKeys.Version("ab12cd34ef56", 3, "png")).IsEqualTo("ab12cd34ef56/v3/current.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task SaveAsync(string key, string text)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await storage.SaveAsync(key, content, "application/octet-stream", CancellationToken.None);
    }

    private async Task<string?> ReadAsync(string key)
    {
        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}