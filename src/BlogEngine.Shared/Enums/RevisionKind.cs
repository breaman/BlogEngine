namespace BlogEngine.Shared.Enums;

/// <summary>
/// Why a post revision was recorded (design 6.7).
/// </summary>
/// <remarks>Values are persisted as integers and must never be renumbered.</remarks>
public enum RevisionKind
{
    /// <summary>Periodic editor autosave; only the latest few are kept per post.</summary>
    Autosave = 0,

    /// <summary>An explicit save by the author; always kept.</summary>
    Manual = 1,

    /// <summary>The content as it was published; always kept.</summary>
    Publish = 2
}
