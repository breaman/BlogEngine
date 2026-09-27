namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Outcome of changing a media item through <see cref="Services.IMediaService"/>: exactly one of
/// <see cref="MediaSaved"/>, <see cref="MediaNotFound"/> or <see cref="MediaInvalid"/>.
/// </summary>
/// <remarks>Expected outcomes are values, as with <see cref="PostSaveResult"/>, so both implementations report them alike.</remarks>
public abstract record MediaSaveResult
{
    /// <summary>The shared not-found result.</summary>
    public static MediaSaveResult NotFound { get; } = new MediaNotFound();

    /// <summary>A validation failure on one field.</summary>
    public static MediaSaveResult Invalid(string field, string message)
    {
        return new MediaInvalid(new Dictionary<string, string[]> { [field] = [message] });
    }
}

/// <summary>The change was saved.</summary>
/// <param name="Item">The item as saved.</param>
public sealed record MediaSaved(MediaItemDto Item) : MediaSaveResult;

/// <summary>No media item has the requested id.</summary>
public sealed record MediaNotFound : MediaSaveResult;

/// <summary>The request failed validation and nothing was saved.</summary>
/// <param name="Errors">Messages keyed by property name.</param>
public sealed record MediaInvalid(IReadOnlyDictionary<string, string[]> Errors) : MediaSaveResult;

/// <summary>
/// Outcome of deleting a media item (design 9.6): <see cref="MediaDeleted"/>, <see cref="MediaDeleteNotFound"/>, or
/// <see cref="MediaInUse"/> when posts use it and the delete wasn't forced.
/// </summary>
public abstract record MediaDeleteResult
{
    /// <summary>The shared deleted result.</summary>
    public static MediaDeleteResult Deleted { get; } = new MediaDeleted();

    /// <summary>The shared not-found result.</summary>
    public static MediaDeleteResult NotFound { get; } = new MediaDeleteNotFound();
}

/// <summary>The item and all its files were deleted.</summary>
public sealed record MediaDeleted : MediaDeleteResult;

/// <summary>No media item has the requested id.</summary>
public sealed record MediaDeleteNotFound : MediaDeleteResult;

/// <summary>Nothing was deleted because posts use the item; repeat with <c>force</c> to delete anyway.</summary>
/// <param name="Posts">The posts that use it.</param>
public sealed record MediaInUse(IReadOnlyList<MediaUsageDto> Posts) : MediaDeleteResult;

/// <summary>
/// Progress of making responsive renditions for existing images (<c>/api/admin/media/renditions</c>, design 9.4, T4.19).
/// </summary>
/// <param name="Processed">Images given fresh renditions by this call.</param>
/// <param name="Remaining">Images whose renditions are still missing or out of date.</param>
public sealed record MediaRenditionProgress(int Processed, int Remaining);