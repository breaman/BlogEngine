namespace BlogEngine.Server.Storage;

/// <summary>
/// Settings for <see cref="FileSystemMediaStorage"/>, bound from the <c>MediaStorage</c> configuration section.
/// </summary>
/// <example>
/// <code>
/// "MediaStorage": { "RootPath": "/var/lib/blogengine/media" }
/// </code>
/// </example>
public sealed class MediaStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MediaStorage";

    /// <summary>Default root, relative to the application's content root.</summary>
    public const string DefaultRootPath = "App_Data/media";

    /// <summary>
    /// Folder that holds the media files. A relative path is resolved against the content root. In production
    /// (or a container) point it at a persistent volume so uploads survive redeployments.
    /// </summary>
    public string RootPath { get; set; } = DefaultRootPath;
}
