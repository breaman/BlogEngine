using BlogEngine.Data.Models;

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
/// This covers URLs that match no endpoint. Public pages that match a route but find nothing call
/// <see cref="RedirectLookup"/> themselves, because <c>NavigationManager.NotFound()</c> has already written
/// the not-found page by the time the response gets back here.
/// </para>
/// <para>
/// API paths are skipped: a missing API resource is a real 404, never a moved page.
/// </para>
/// </remarks>
public sealed class RedirectFallbackMiddleware(RequestDelegate next)
{
    /// <summary>Runs the rest of the pipeline, then replaces an empty GET/HEAD 404 with a stored redirect.</summary>
    public async Task InvokeAsync(HttpContext context, RedirectLookup redirects)
    {
        await next(context);

        var request = context.Request;
        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || context.Response.HasStarted
            || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || request.Path.StartsWithSegments(Endpoints.ApiStatusCodePagesExtensions.ApiPathPrefix))
        {
            return;
        }

        if (await redirects.FindAsync(request.Path, request.QueryString, context.RequestAborted) is not { } redirect)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = redirect.StatusCode;
        context.Response.Headers.Location = redirect.Location;
    }
}