using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only middleware, first in the pipeline, that sets the connection's remote address from the
/// <see cref="HeaderName"/> request header. The in-memory test server has no client address, so without this every
/// test would share one comment rate-limit partition and spam-guard IP hash.
/// </summary>
internal sealed class TestClientIpStartupFilter : IStartupFilter
{
    /// <summary>Request header carrying the address a test wants to appear from.</summary>
    public const string HeaderName = "X-Test-Client-IP";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[HeaderName], out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }
}