using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.Client.Services;

/// <summary>
/// Adds the antiforgery request token to state-changing requests sent by the WebAssembly
/// <see cref="HttpClient"/>, so the cookie-authenticated <c>/api/admin</c> endpoints accept them
/// (design 7.4).
/// </summary>
/// <remarks>
/// Blazor persists the token into component state when the page is prerendered, and
/// <see cref="AntiforgeryStateProvider"/> reads it back in the browser, so no extra round trip is needed.
/// The token is only sent to <paramref name="baseAddress"/>'s origin, never to third-party hosts.
/// </remarks>
/// <param name="antiforgery">Source of the current antiforgery request token.</param>
/// <param name="baseAddress">The app's origin; requests to any other origin are sent unchanged.</param>
public sealed class AntiforgeryHandler(AntiforgeryStateProvider antiforgery, Uri baseAddress) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RequiresToken(request.Method)
            && IsSameOrigin(request.RequestUri)
            && !request.Headers.Contains(AntiforgeryHeaders.RequestToken)
            && antiforgery.GetAntiforgeryToken() is { Value: { Length: > 0 } token })
        {
            request.Headers.Add(AntiforgeryHeaders.RequestToken, token);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Mirrors the server's filter: safe methods don't need the token.</summary>
    private static bool RequiresToken(HttpMethod method)
    {
        return method != HttpMethod.Get && method != HttpMethod.Head
            && method != HttpMethod.Options && method != HttpMethod.Trace;
    }

    /// <summary>Relative URIs resolve against the base address, so only absolute URIs need comparing.</summary>
    private bool IsSameOrigin(Uri? requestUri)
    {
        return requestUri is null
            || !requestUri.IsAbsoluteUri
            || Uri.Compare(requestUri, baseAddress, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}