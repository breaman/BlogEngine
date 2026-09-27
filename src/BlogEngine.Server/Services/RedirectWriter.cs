using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Records permanent redirects when a public URL moves (design 6.7, P16).
/// </summary>
internal static class RedirectWriter
{
    /// <summary>
    /// Adds a 301 from <paramref name="fromPath"/> to <paramref name="toPath"/> to the context (saved with the
    /// caller's <c>SaveChanges</c>), keeping the table free of chains and loops.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Existing redirects that pointed at <paramref name="fromPath"/> are repointed at
    /// <paramref name="toPath"/>, so every old URL reaches the newest one in a single hop.</item>
    /// <item>A redirect <em>from</em> <paramref name="toPath"/> is removed: the URL is live again (for example
    /// a slug changed back), and keeping it would create a loop.</item>
    /// </list>
    /// </remarks>
    public static async Task AddAsync(ApplicationDbContext dbContext, string fromPath, string toPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fromPath, toPath, StringComparison.Ordinal))
        {
            return;
        }

        var affected = await dbContext.Redirects
            .Where(r => r.ToPath == fromPath || r.FromPath == fromPath || r.FromPath == toPath)
            .ToListAsync(cancellationToken);

        foreach (var redirect in affected)
        {
            if (redirect.FromPath == toPath)
            {
                dbContext.Redirects.Remove(redirect);
            }
            else
            {
                redirect.ToPath = toPath;
            }
        }

        // The old URL was live until now, so it normally has no redirect yet; reuse one if it does.
        if (!affected.Any(r => r.FromPath == fromPath))
        {
            dbContext.Redirects.Add(new Redirect { FromPath = fromPath, ToPath = toPath, StatusCode = StatusCodes.Status301MovedPermanently });
        }
    }
}