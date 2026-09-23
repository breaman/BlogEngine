namespace BlogEngine.Server.Endpoints;

/// <summary>
/// The absolute root URL of the site for the current request, used where public output needs absolute URLs:
/// feeds, the sitemap and <c>robots.txt</c> (design 16).
/// </summary>
/// <remarks>
/// Built from the request's scheme and host, so it follows whatever host name the site is reached on. Behind a
/// reverse proxy, forwarded headers must be enabled so these reflect the public address.
/// </remarks>
public static class PublicSiteUrl
{
    /// <summary>The site root, with a trailing slash, such as <c>https://blog.example/</c>.</summary>
    public static Uri Root(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Uri($"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/");
    }
}
