using Microsoft.AspNetCore.Diagnostics;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Keeps API responses out of the HTML status code pages.
/// </summary>
/// <remarks>
/// <c>UseStatusCodePagesWithReExecute("/not-found")</c> re-runs the pipeline for every empty 4xx/5xx
/// response using the <em>original</em> HTTP method. For an API that turns a 401, 403 or 404 into the
/// not-found page's answer to a PUT, DELETE or POST (405, or a 400 from Blazor's form antiforgery check),
/// hiding the real status from the WebAssembly client. API callers need the bare status code (or the
/// problem details the endpoint wrote), so status code pages are switched off for <c>/api</c> requests.
/// </remarks>
public static class ApiStatusCodePagesExtensions
{
    /// <summary>Path prefix of every JSON API endpoint.</summary>
    public const string ApiPathPrefix = "/api";

    /// <summary>
    /// Disables status code pages for <c>/api</c> requests. Must be registered after
    /// <c>UseStatusCodePages*</c>, which is what adds the feature this switches off.
    /// </summary>
    public static IApplicationBuilder UseApiWithoutStatusCodePages(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(ApiPathPrefix)
                && context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            {
                statusCodePages.Enabled = false;
            }

            return next(context);
        });
    }
}