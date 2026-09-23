namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Outcome of a post operation in <see cref="Services.IPostAdminService"/>: exactly one of
/// <see cref="PostSaved"/>, <see cref="PostNotFound"/>, <see cref="PostConflict"/> or <see cref="PostInvalid"/>.
/// </summary>
/// <remarks>
/// Expected outcomes are values rather than exceptions so both implementations (database and HTTP) report
/// them the same way and callers handle them with a switch expression.
/// </remarks>
/// <example>
/// <code>
/// var message = await posts.UpdateAsync(id, post) switch
/// {
///     PostSaved saved => $"Saved {saved.Post.Title}",
///     PostConflict => "This post was changed in another tab. Reload or overwrite?",
///     PostNotFound => "The post no longer exists.",
///     PostInvalid invalid => string.Join(" ", invalid.Errors.SelectMany(e => e.Value)),
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </example>
public abstract record PostSaveResult
{
    /// <summary>The shared not-found result.</summary>
    public static PostSaveResult NotFound { get; } = new PostNotFound();

    /// <summary>The shared conflict result.</summary>
    public static PostSaveResult Conflict { get; } = new PostConflict();

    /// <summary>A validation failure on one field.</summary>
    public static PostSaveResult Invalid(string field, string message)
    {
        return new PostInvalid(new Dictionary<string, string[]> { [field] = [message] });
    }
}

/// <summary>The operation succeeded.</summary>
/// <param name="Post">The post as saved, with its new <see cref="PostEditDto.RowVersion"/>.</param>
public sealed record PostSaved(PostEditDto Post) : PostSaveResult;

/// <summary>No post (outside the trash) has the requested id.</summary>
public sealed record PostNotFound : PostSaveResult;

/// <summary>
/// The post changed since the caller loaded it (its <see cref="PostEditDto.RowVersion"/> is stale), so
/// nothing was saved.
/// </summary>
public sealed record PostConflict : PostSaveResult;

/// <summary>The request failed validation and nothing was saved.</summary>
/// <param name="Errors">Messages keyed by property name.</param>
public sealed record PostInvalid(IReadOnlyDictionary<string, string[]> Errors) : PostSaveResult;
