namespace BlogEngine.Server.Services.Export;

/// <summary>
/// A write-only, non-seekable stream that collects what a <see cref="System.IO.Compression.ZipArchive"/> writes and
/// passes it on to the real output asynchronously, one entry at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Even through the .NET 10 async zip APIs, closing an entry flushes its compressor with a synchronous
/// <c>Write</c>, and ASP.NET Core rejects synchronous writes to the response body. The archive writes here instead; the
/// exporter calls <see cref="DrainToAsync"/> after each entry, so the response is written only asynchronously and memory
/// holds at most one entry (bounded by the largest upload) rather than the whole zip.
/// </para>
/// <para>
/// The stream reports <see cref="CanSeek"/> as <see langword="false"/>, so the archive writes sizes in data descriptors
/// after each entry instead of seeking back to patch its header, which could not work once bytes have been drained.
/// </para>
/// </remarks>
public sealed class ZipOutputSpool : Stream
{
    private readonly MemoryStream _pending = new();
    private long _written;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <summary>Bytes written so far, drained or not; the archive reads it to record entry offsets.</summary>
    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    /// <summary>Writes the bytes collected since the last drain to <paramref name="destination"/>, and forgets them.</summary>
    public async Task DrainToAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (_pending.Length == 0)
        {
            return;
        }

        await destination.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
        _pending.SetLength(0);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _pending.Write(buffer);
        _written += buffer.Length;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Nothing to do: bytes leave only through DrainToAsync.
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pending.Dispose();
        }

        base.Dispose(disposing);
    }
}
