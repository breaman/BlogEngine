using BlogEngine.Server.Services.Public;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Output cache policies for the public XML and text endpoints (design 11): feeds, the sitemap and
/// <c>robots.txt</c>. Post pages are deliberately not output-cached, since they will carry per-visitor
/// antiforgery tokens for the comment form.
/// </summary>
/// <remarks>
/// Entries are tagged so <see cref="CacheInvalidator"/> can evict them the moment the data changes; the
/// expiration is only a safety net. The cache key includes the host, so absolute URLs stay correct when the site
/// is reached under more than one name.
/// </remarks>
public static class PublicOutputCachePolicies
{
    /// <summary>Responses built from the visible posts (feeds, sitemap).</summary>
    public const string Posts = "public-posts";

    /// <summary>Responses built only from the settings (<c>robots.txt</c>).</summary>
    public const string Settings = "public-settings";

    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(10);

    /// <summary>Registers the output cache and the public policies.</summary>
    public static IServiceCollection AddPublicOutputCache(this IServiceCollection services)
    {
        return services.AddOutputCache(options =>
        {
            options.AddPolicy(Posts, policy => policy.Expire(Expiration).Tag(PublicCacheTags.Posts));
            options.AddPolicy(Settings, policy => policy.Expire(Expiration).Tag(PublicCacheTags.Settings));
        });
    }
}