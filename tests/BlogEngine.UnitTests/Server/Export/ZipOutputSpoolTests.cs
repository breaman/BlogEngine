using System.IO.Compression;
using System.Text;

using BlogEngine.Server.Services.Export;

namespace BlogEngine.UnitTests.Server.Export;

/// <summary>
/// Tests <see cref="ZipOutputSpool"/>: what a zip archive writes, synchronously or not, reaches the output only when
/// drained, and the drained bytes form a valid zip (T4.24).
/// </summary>
public class ZipOutputSpoolTests
{
    /// <summary>Bytes wait in the spool until drained, then are passed on once; the position counts every byte written.</summary>
    [Test]
    public async Task DrainToAsync_PassesPendingBytesOnce()
    {
        await using var spool = new ZipOutputSpool();
        using var output = new MemoryStream();

        spool.Write("ab"u8);
        await spool.WriteAsync("cd"u8.ToArray());
        var beforeDrain = output.Length;
        await spool.DrainToAsync(output, CancellationToken.None);
        await spool.DrainToAsync(output, CancellationToken.None);
        spool.Write("e"u8);
        await spool.DrainToAsync(output, CancellationToken.None);

        await Assert.That(beforeDrain).IsEqualTo(0);
        await Assert.That(Encoding.ASCII.GetString(output.ToArray())).IsEqualTo("abcde");
        await Assert.That(spool.Position).IsEqualTo(5);
        await Assert.That(spool.CanSeek).IsFalse();
    }

    /// <summary>A zip written into the spool and drained to an output that only accepts async writes opens with its entries.</summary>
    [Test]
    public async Task ZipWrittenThroughSpool_IsValid()
    {
        await using var spool = new ZipOutputSpool();
        using var output = new AsyncOnlyStream();
        var zip = await ZipArchive.CreateAsync(spool, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null);

        foreach (var (name, text) in new[] { ("a.md", "First"), ("folder/b.json", "{\"x\":1}") })
        {
            await using (var stream = await zip.CreateEntry(name, CompressionLevel.Optimal).OpenAsync())
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
            }

            await spool.DrainToAsync(output, CancellationToken.None);
        }

        await zip.DisposeAsync();
        await spool.DrainToAsync(output, CancellationToken.None);

        output.Position = 0;
        using var read = new ZipArchive(output, ZipArchiveMode.Read);
        using var reader = new StreamReader(read.GetEntry("folder/b.json")!.Open());
        await Assert.That(read.Entries.Select(e => e.FullName)).IsEquivalentTo(["a.md", "folder/b.json"]);
        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("{\"x\":1}");
    }

    /// <summary>A memory stream that rejects synchronous writes from callers, like an ASP.NET Core response body.</summary>
    private sealed class AsyncOnlyStream : MemoryStream
    {
        // MemoryStream's span Write calls the array Write, so the async overrides mark their own writes as allowed.
        private bool _writingAsync;

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureAsync();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureAsync();
            base.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writingAsync = true;
            try
            {
                base.Write(buffer.Span);
            }
            finally
            {
                _writingAsync = false;
            }

            return ValueTask.CompletedTask;
        }

        private void EnsureAsync()
        {
            if (!_writingAsync)
            {
                throw new InvalidOperationException("Synchronous operations are disallowed.");
            }
        }
    }
}
