using System.Globalization;
using System.Threading.RateLimiting;

using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// The search rate limit (design 12.4, P9): a fixed window of <see cref="PermitLimit"/> searches per
/// <see cref="Window"/> for each client, because every search runs an uncached <c>LIKE</c> scan.
/// </summary>
/// <remarks>
/// <para>
/// Attached to the search page with <c>[EnableRateLimiting(PolicyName)]</c>. Partitions are keyed by the same salted IP
/// hash as the comment limit (<see cref="CommentIpHasher"/>), so raw addresses are never kept, even in memory. The limit
/// is generous for a person and only stops scripted scraping.
/// </para>
/// <para>
/// The policy has its own rejection handler, which ASP.NET Core uses instead of the global one (the comment limit's), and
/// answers with a short page explaining when to search again.
/// </para>
/// </remarks>
public sealed class SearchRateLimiting : IRateLimiterPolicy<string>
{
    /// <summary>Name of the rate limiting policy on the search page.</summary>
    public const string PolicyName = "search";

    /// <summary>Searches allowed per client in each <see cref="Window"/>.</summary>
    public const int PermitLimit = 30;

    /// <summary>The length of the fixed window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => WriteRejectionAsync;

    /// <inheritdoc />
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var ipHash = httpContext.RequestServices.GetRequiredService<CommentIpHasher>().Hash(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(ipHash, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    /// <summary>Answers a rejected search with a 429 page and a <c>Retry-After</c> header.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<SearchRateLimiting>()
            .LogWarning("A search was rate limited.");

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : Window;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        http.Response.ContentType = "text/html; charset=utf-8";

        await http.Response.WriteAsync($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <meta name="robots" content="noindex" />
                <title>Too many searches</title>
                <link rel="stylesheet" href="/css/site.css" />
            </head>
            <body>
                <main class="container py-5" style="max-width: 40rem;">
                    <h1 class="h3">Too many searches</h1>
                    <p>You've searched a lot in a short time. Please wait a minute and then try again.</p>
                    <p><a href="{{SitePaths.Home}}">Back to the blog</a></p>
                </main>
            </body>
            </html>
            """, cancellationToken);
    }
}
