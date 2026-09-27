namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Structured log events for the media library (design 18, T2.13). Each has a stable event id and name, so the
/// events can be found in Serilog or OpenTelemetry by <c>EventId.Name</c> and filtered on their properties.
/// </summary>
internal static partial class MediaLog
{
    /// <summary>An image was added to the library.</summary>
    [LoggerMessage(EventId = 2001, EventName = "MediaUploaded", Level = LogLevel.Information,
        Message = "Media {MediaId} ({PublicId}) uploaded as {FileName}: {ContentType}, {Width}x{Height}, {SizeBytes} bytes")]
    public static partial void MediaUploaded(ILogger logger, int mediaId, string publicId, string fileName,
        string contentType, int width, int height, long sizeBytes);

    /// <summary>An image was edited, producing a new version.</summary>
    [LoggerMessage(EventId = 2002, EventName = "MediaEdited", Level = LogLevel.Information,
        Message = "Media {MediaId} ({PublicId}) edited to version {Version}: {Width}x{Height}, {SizeBytes} bytes; {PostCount} posts re-rendered")]
    public static partial void MediaEdited(ILogger logger, int mediaId, string publicId, int version,
        int width, int height, long sizeBytes, int postCount);

    /// <summary>An image was deleted from the library.</summary>
    [LoggerMessage(EventId = 2003, EventName = "MediaDeleted", Level = LogLevel.Information,
        Message = "Media {MediaId} ({PublicId}) deleted; {PostCount} posts that used it were re-rendered")]
    public static partial void MediaDeleted(ILogger logger, int mediaId, string publicId, int postCount);

    /// <summary>An upload was rejected (not an image, too large, unsupported format).</summary>
    [LoggerMessage(EventId = 2004, EventName = "MediaUploadRejected", Level = LogLevel.Warning,
        Message = "Upload of {FileName} ({SizeBytes} bytes) was rejected: {Reason}")]
    public static partial void MediaUploadRejected(ILogger logger, string fileName, long sizeBytes, string reason);

    /// <summary>"Save as copy" made a new library item from an edited original.</summary>
    [LoggerMessage(EventId = 2005, EventName = "MediaCopied", Level = LogLevel.Information,
        Message = "Media {MediaId} ({PublicId}) saved as an edited copy of media {SourceMediaId}: {Width}x{Height}, {SizeBytes} bytes")]
    public static partial void MediaCopied(ILogger logger, int mediaId, string publicId, int sourceMediaId, int width, int height,
        long sizeBytes);
}