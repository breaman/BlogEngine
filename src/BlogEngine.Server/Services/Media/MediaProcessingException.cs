namespace BlogEngine.Server.Services.Media;

/// <summary>
/// An upload or edit that can't be processed, with a message that can be shown to the author as is
/// (for example "HEIC photos aren't supported").
/// </summary>
public sealed class MediaProcessingException(string message, Exception? innerException = null)
    : Exception(message, innerException);