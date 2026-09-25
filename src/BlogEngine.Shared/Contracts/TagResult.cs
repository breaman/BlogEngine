namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Outcome of a tag management operation in <see cref="Services.ITagService"/> (design 6.4, O3): exactly one of
/// <see cref="TagSaved"/>, <see cref="TagDeleted"/>, <see cref="TagNotFound"/>, <see cref="TagConflict"/> or
/// <see cref="TagInvalid"/>.
/// </summary>
/// <remarks>
/// Expected outcomes are values rather than exceptions, so the database and HTTP implementations report them the same
/// way and callers handle them with a switch expression.
/// </remarks>
/// <example>
/// <code>
/// var message = await tags.MergeAsync(sourceId, targetId) switch
/// {
///     TagSaved saved => $"Merged into {saved.Tag.Name}.",
///     TagNotFound => "The tag no longer exists.",
///     TagConflict conflict => conflict.Message,
///     TagInvalid invalid => string.Join(" ", invalid.Errors.SelectMany(e => e.Value)),
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </example>
public abstract record TagResult
{
    /// <summary>The shared not-found result.</summary>
    public static TagResult NotFound { get; } = new TagNotFound();

    /// <summary>The shared deleted result.</summary>
    public static TagResult Deleted { get; } = new TagDeleted();

    /// <summary>A validation failure on one field (an empty field name for the request as a whole).</summary>
    public static TagResult Invalid(string field, string message)
    {
        return new TagInvalid(new Dictionary<string, string[]> { [field] = [message] });
    }
}

/// <summary>The tag was renamed, or another tag was merged into it.</summary>
/// <param name="Tag">The tag as saved, with its current usage counts.</param>
public sealed record TagSaved(TagAdminDto Tag) : TagResult;

/// <summary>The tag was deleted.</summary>
public sealed record TagDeleted : TagResult;

/// <summary>No tag has the requested id (for a merge: the tag or its target).</summary>
public sealed record TagNotFound : TagResult;

/// <summary>
/// The operation can't be done in the tag's current state, for example a rename to a name another tag has, or deleting
/// a tag that posts still use. Nothing was changed.
/// </summary>
/// <param name="Message">What is in the way and what to do instead, ready to show.</param>
public sealed record TagConflict(string Message) : TagResult;

/// <summary>The request failed validation and nothing was saved.</summary>
/// <param name="Errors">Messages keyed by property name.</param>
public sealed record TagInvalid(IReadOnlyDictionary<string, string[]> Errors) : TagResult;
