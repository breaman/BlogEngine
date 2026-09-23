using Microsoft.AspNetCore.Antiforgery;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Rejects state-changing requests (anything but GET, HEAD, OPTIONS and TRACE) that don't carry a valid
/// antiforgery token (design 7.4). The admin API authenticates with the Identity cookie, which a browser
/// attaches to cross-site requests too, so the token is what proves the call came from the admin UI.
/// </summary>
/// <remarks>
/// Minimal API endpoints only get antiforgery validation automatically when they bind form data, and the
/// admin API takes JSON, so the check is made explicitly here. The WebAssembly client sends the token in
/// the <see cref="Shared.Security.AntiforgeryHeaders.RequestToken"/> header.
/// </remarks>
public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (RequiresValidation(httpContext.Request.Method) && !await antiforgery.IsRequestValidAsync(httpContext))
        {
            return TypedResults.Problem(
                title: "Invalid antiforgery token",
                detail: "The request is missing a valid antiforgery token. Reload the page and try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }

    /// <summary>Safe methods don't change state, so they don't need the token.</summary>
    private static bool RequiresValidation(string method)
    {
        return !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
    }
}
