using BlogEngine.Server.Services.Export;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for exporting the blog (design 7.4, 17, O7): <c>GET /export</c> streams <c>blog-export-{date}.zip</c>.
/// </summary>
/// <remarks>
/// A GET, so the settings page can offer it as a plain download link; it changes nothing, so it needs no antiforgery
/// token, and the admin-only policy of the group still applies. The zip is written while it downloads (see
/// <see cref="BlogExporter"/>), so an error part-way can only abort the download, not turn into an error status.
/// </remarks>
public static class AdminExportEndpoints
{
    /// <summary>Maps the export endpoint onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminExportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/export", async (BlogExporter exporter, HttpContext context) =>
        {
            var fileName = await exporter.GetFileNameAsync(context.RequestAborted);

            // The export holds unpublished drafts and commenters' emails; it must never be kept by a cache.
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Stream(output => exporter.WriteAsync(output, context.RequestAborted), "application/zip", fileName);
        });

        return group;
    }
}