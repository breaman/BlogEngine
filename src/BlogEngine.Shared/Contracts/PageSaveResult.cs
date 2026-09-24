namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Outcome of a page operation in <see cref="Services.IPageAdminService"/>: exactly one of <see cref="PageSaved"/>,
/// <see cref="PageNotFound"/> or <see cref="PageInvalid"/>.
/// </summary>
/// <remarks>
/// Like <see cref="PostSaveResult"/>, expected outcomes are values so the database and HTTP implementations report them
/// the same way. Pages have no concurrency token, so there is no conflict outcome: the last save wins.
/// </remarks>
/// <example>
/// <code>
/// var message = await pages.UpdateAsync(id, page) switch
/// {
///     PageSaved saved => $"Saved {saved.Page.Title}",
///     PageNotFound => "The page no longer exists.",
///     PageInvalid invalid => string.Join(" ", invalid.Errors.SelectMany(e => e.Value)),
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </example>
public abstract record PageSaveResult
{
    /// <summary>The shared not-found result.</summary>
    public static PageSaveResult NotFound { get; } = new PageNotFound();
}

/// <summary>The operation succeeded.</summary>
/// <param name="Page">The page as saved.</param>
public sealed record PageSaved(PageEditDto Page) : PageSaveResult;

/// <summary>No page has the requested id.</summary>
public sealed record PageNotFound : PageSaveResult;

/// <summary>The request failed validation and nothing was saved.</summary>
/// <param name="Errors">Messages keyed by property name.</param>
public sealed record PageInvalid(IReadOnlyDictionary<string, string[]> Errors) : PageSaveResult;
