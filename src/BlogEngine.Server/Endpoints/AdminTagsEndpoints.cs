using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for tags (design 6.4, 7.4): autocomplete for the editor, and tag management (O3). Management handlers
/// delegate to <see cref="ITagService"/> and map its <see cref="TagResult"/> to HTTP: 200 with the tag, 204 after a
/// delete, 404, 409 with a problem whose detail says what is in the way, or a 400 validation problem.
/// </summary>
public static class AdminTagsEndpoints
{
    /// <summary>Maps the tag endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminTagsEndpoints(this RouteGroupBuilder group)
    {
        var tags = group.MapGroup("/tags");

        tags.MapGet("/", SearchTagsAsync);
        tags.MapGet("/all", async (ITagService service, CancellationToken ct) => TypedResults.Ok(await service.GetAllAsync(ct)));
        tags.MapPut("/{id:int}", (int id, UpdateTagRequest request, ITagService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UpdateAsync(id, request, ct)));
        tags.MapPost("/{id:int}/merge/{targetId:int}", (int id, int targetId, ITagService service, CancellationToken ct) =>
            ToHttpResultAsync(service.MergeAsync(id, targetId, ct)));
        tags.MapDelete("/{id:int}", (int id, ITagService service, CancellationToken ct) =>
            ToHttpResultAsync(service.DeleteAsync(id, ct)));

        return group;
    }

    /// <summary>Tag autocomplete: <c>GET /tags?search=</c>, the top 10 matches.</summary>
    private static async Task<Ok<IReadOnlyList<TagDto>>> SearchTagsAsync(string? search, ITagService tagService,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await tagService.SearchAsync(search, cancellationToken));
    }

    /// <summary>Maps a tag management outcome to its HTTP response.</summary>
    private static async Task<Results<Ok<TagAdminDto>, NoContent, NotFound, ProblemHttpResult, ValidationProblem>> ToHttpResultAsync(
        Task<TagResult> operation)
    {
        return await operation switch
        {
            TagSaved saved => TypedResults.Ok(saved.Tag),
            TagDeleted => TypedResults.NoContent(),
            TagNotFound => TypedResults.NotFound(),
            TagConflict conflict => TypedResults.Problem(title: "The tag can't be changed that way.", detail: conflict.Message,
                statusCode: StatusCodes.Status409Conflict),
            TagInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Unknown tag result {other.GetType().Name}.")
        };
    }
}