using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for tags (design 7.4). For now only autocomplete; rename, merge and delete arrive with tag
/// management (T4.22).
/// </summary>
public static class AdminTagsEndpoints
{
    /// <summary>Maps the tag endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminTagsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/tags", SearchTagsAsync);

        return group;
    }

    /// <summary>Tag autocomplete: <c>GET /tags?search=</c>, the top 10 matches.</summary>
    private static async Task<Ok<IReadOnlyList<TagDto>>> SearchTagsAsync(string? search, ITagService tagService,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await tagService.SearchAsync(search, cancellationToken));
    }
}
