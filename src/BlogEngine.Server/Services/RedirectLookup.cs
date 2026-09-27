using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Finds the stored redirect for a URL path that has no content (design 6.7, 7.1), and counts the hit.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="RedirectFallbackMiddleware"/> for requests no endpoint answered, and by public pages that
/// matched a route but found nothing, such as the post page: in static SSR, <c>NavigationManager.NotFound()</c>
/// renders the not-found page straight away, so the middleware never sees an empty 404 for them.
/// </para>
/// <para>
/// Paths are matched case-insensitively (through the database collation) and without a trailing slash, the
/// canonical form in which redirects are stored. Lookups use their own scope, so a page can call this while
/// other components of the same static SSR render are using the request's <see cref="ApplicationDbContext"/>.
/// </para>
/// </remarks>
public sealed class RedirectLookup(IServiceScopeFactory scopeFactory)
{
    /// <summary>A stored redirect: where to send the client, and with which status code.</summary>
    /// <param name="Location">Target path, including the request's query string.</param>
    /// <param name="StatusCode">HTTP status code, normally 301.</param>
    public sealed record Match(string Location, int StatusCode);

    /// <summary>
    /// The redirect stored for <paramref name="path"/>, with <paramref name="queryString"/> carried over to the
    /// target, or <see langword="null"/> if there is none. A found redirect's hit count is incremented.
    /// </summary>
    /// <param name="path">The request path, such as <c>/posts/2026/09/22/old-slug</c>.</param>
    /// <param name="queryString">The request's query string, appended to the target.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<Match?> FindAsync(PathString path, QueryString queryString, CancellationToken cancellationToken = default)
    {
        var fromPath = path.Value?.TrimEnd('/');
        if (string.IsNullOrEmpty(fromPath) || fromPath.Length > FieldLengths.Url)
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var redirect = await dbContext.Redirects
            .AsNoTracking()
            .Where(r => r.FromPath == fromPath)
            .Select(r => new { r.Id, r.ToPath, r.StatusCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (redirect is null)
        {
            return null;
        }

        await dbContext.Redirects
            .Where(r => r.Id == redirect.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HitCount, r => r.HitCount + 1), cancellationToken);

        return new Match(redirect.ToPath + queryString, redirect.StatusCode);
    }
}