using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Answers requests that would otherwise be a 404 with a redirect from the <see cref="Redirect"/> table
/// (design 6.7, 7.1), so URLs of posts whose slug or date changed keep working.
/// </summary>
/// <remarks>
/// <para>
/// The table is only consulted after the rest of the pipeline has produced a 404 with nothing written yet,
/// so ordinary requests never pay for the lookup. It must be registered after
/// <c>UseStatusCodePagesWithReExecute</c> so it sees the 404 before the not-found page is rendered.
/// </para>
/// <para>
/// API paths are skipped: a missing API resource is a real 404, never a moved page. Paths are matched
/// case-insensitively (through the database collation) and without a trailing slash,
/// matching the canonical form in which redirects are stored.
/// </para>
/// </remarks>
public sealed class RedirectFallbackMiddleware(RequestDelegate next)
{
    /// <summary>Runs the rest of the pipeline, then replaces an empty GET/HEAD 404 with a stored redirect.</summary>
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        await next(context);

        var request = context.Request;
        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || context.Response.HasStarted
            || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || !request.Path.HasValue
            || request.Path.StartsWithSegments(Endpoints.ApiStatusCodePagesExtensions.ApiPathPrefix))
        {
            return;
        }

        var path = request.Path.Value!.TrimEnd('/');
        if (path.Length == 0 || path.Length > Shared.Common.FieldLengths.Url)
        {
            return;
        }

        var redirect = await dbContext.Redirects
            .AsNoTracking()
            .Where(r => r.FromPath == path)
            .Select(r => new { r.Id, r.ToPath, r.StatusCode })
            .FirstOrDefaultAsync(context.RequestAborted);
        if (redirect is null)
        {
            return;
        }

        await dbContext.Redirects
            .Where(r => r.Id == redirect.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HitCount, r => r.HitCount + 1), context.RequestAborted);

        context.Response.Clear();
        context.Response.StatusCode = redirect.StatusCode;
        context.Response.Headers.Location = redirect.ToPath + request.QueryString;
    }
}
