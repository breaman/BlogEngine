using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// The comment rate limit (design 8.3, 12.4, T3.5): a fixed window of <see cref="CommentOptions.RateLimitPermits"/>
/// comment posts per <see cref="CommentOptions.RateLimitWindow"/> for each commenter IP hash, enforced by the ASP.NET
/// Core rate limiting middleware before the form handler runs.
/// </summary>
/// <remarks>
/// The policy is attached to the post page with <c>[EnableRateLimiting(PolicyName)]</c>. Only POSTs (comment
/// submissions) are limited; reading the page is not. Partitions are keyed by the salted IP hash, so raw addresses are
/// never kept, even in memory. A rejected post gets a small, friendly 429 page with a <c>Retry-After</c> header.
/// </remarks>
public static class CommentRateLimiting
{
    /// <summary>Name of the rate limiting policy on the post page.</summary>
    public const string PolicyName = "comments";

    /// <summary>Registers the comment rate limiting policy.</summary>
    public static IServiceCollection AddCommentRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
            options.AddPolicy(PolicyName, Partition);
        });

        return services;
    }

    /// <summary>A fixed window per IP hash for comment posts; no limit for anything else.</summary>
    private static RateLimitPartition<string> Partition(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<CommentOptions>>().Value;
        var ipHash = services.GetRequiredService<CommentIpHasher>().Hash(context.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(ipHash, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.RateLimitPermits),
            Window = options.RateLimitWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    /// <summary>Answers a rejected comment post with a short page explaining when to try again.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        CommentLog.CommentRateLimited(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CommentRateLimiting)), http.Request.Path);

        var window = http.RequestServices.GetRequiredService<IOptions<CommentOptions>>().Value.RateLimitWindow;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : window;
        var minutes = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes));
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        http.Response.ContentType = "text/html; charset=utf-8";

        var backLink = WebUtility.HtmlEncode(http.Request.Path.Value ?? "/");
        var wait = minutes == 1 ? "a minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
        await http.Response.WriteAsync($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <meta name="robots" content="noindex" />
                <title>Slow down a little</title>
                <link rel="stylesheet" href="/css/site.css" />
            </head>
            <body>
                <main class="container py-5" style="max-width: 40rem;">
                    <h1 class="h3">Slow down a little</h1>
                    <p>You've posted several comments in a short time. Please wait {{wait}} and then try again.</p>
                    <p><a href="{{backLink}}">Back to the post</a></p>
                </main>
            </body>
            </html>
            """, cancellationToken);
    }
}
