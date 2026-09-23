using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for posts (design 7.4): listing, editing, autosave, publishing, trash and slug checks. Each
/// handler delegates to <see cref="IPostAdminService"/> and maps its <see cref="PostSaveResult"/> to HTTP:
/// 200 with the saved post, 404, 409 for a stale <c>RowVersion</c>, or a 400 validation problem.
/// </summary>
public static class AdminPostsEndpoints
{
    /// <summary>Maps the post endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminPostsEndpoints(this RouteGroupBuilder group)
    {
        var posts = group.MapGroup("/posts");

        posts.MapGet("/", GetPostsAsync);
        posts.MapGet("/{id:int}", GetPostAsync).WithName(GetPostRouteName);
        posts.MapPost("/", CreatePostAsync);
        posts.MapPut("/{id:int}", (int id, PostEditDto post, IPostAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UpdateAsync(id, post, ct)));
        posts.MapPost("/{id:int}/autosave", (int id, PostEditDto post, IPostAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.AutosaveAsync(id, post, ct)));
        posts.MapPost("/{id:int}/publish", (int id, PublishPostRequest request, IPostAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.PublishAsync(id, request, ct)));
        posts.MapPost("/{id:int}/unpublish", (int id, UnpublishPostRequest? request, IPostAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UnpublishAsync(id, request ?? new UnpublishPostRequest(), ct)));
        posts.MapDelete("/{id:int}", DeletePostAsync);
        posts.MapPost("/slug-check", CheckSlugAsync);

        return group;
    }

    /// <summary>Route name of <c>GET /posts/{id}</c>, used for the <c>Location</c> header on create.</summary>
    private const string GetPostRouteName = "AdminGetPost";

    /// <summary>Lists posts, filtered and paged by the query string.</summary>
    private static async Task<Ok<PagedResult<PostSummaryDto>>> GetPostsAsync([AsParameters] PostListQuery query,
        IPostAdminService service, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.GetPostsAsync(query, cancellationToken));
    }

    /// <summary>Loads one post for the editor.</summary>
    private static async Task<Results<Ok<PostEditDto>, NotFound>> GetPostAsync(int id, IPostAdminService service,
        CancellationToken cancellationToken)
    {
        return await service.GetPostAsync(id, cancellationToken) is { } post
            ? TypedResults.Ok(post)
            : TypedResults.NotFound();
    }

    /// <summary>Creates a draft; 201 with its location, or a 400 validation problem.</summary>
    private static async Task<Results<CreatedAtRoute<PostEditDto>, ValidationProblem>> CreatePostAsync(
        PostEditDto post, IPostAdminService service, CancellationToken cancellationToken)
    {
        return await service.CreateAsync(post, cancellationToken) switch
        {
            PostSaved saved => TypedResults.CreatedAtRoute(saved.Post, GetPostRouteName, new { id = saved.Post.Id }),
            PostInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Creating a post cannot result in {other.GetType().Name}.")
        };
    }

    /// <summary>Moves a post to the trash.</summary>
    private static async Task<Results<NoContent, NotFound>> DeletePostAsync(int id, IPostAdminService service,
        CancellationToken cancellationToken)
    {
        return await service.DeleteAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Checks a slug for the editor's inline validation.</summary>
    private static async Task<Ok<SlugCheckResult>> CheckSlugAsync(SlugCheckRequest request, IPostAdminService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.CheckSlugAsync(request, cancellationToken));
    }

    /// <summary>Maps a save outcome to its HTTP response.</summary>
    private static async Task<Results<Ok<PostEditDto>, NotFound, ProblemHttpResult, ValidationProblem>> ToHttpResultAsync(
        Task<PostSaveResult> operation)
    {
        return await operation switch
        {
            PostSaved saved => TypedResults.Ok(saved.Post),
            PostNotFound => TypedResults.NotFound(),
            PostConflict => TypedResults.Problem(
                title: "The post was changed elsewhere.",
                detail: "This post was changed in another tab or by another session. Reload it, or overwrite with your version.",
                statusCode: StatusCodes.Status409Conflict),
            PostInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Unknown post save result {other.GetType().Name}.")
        };
    }
}
