using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for posts (design 7.4): listing, editing, autosave, publishing, trash (move, restore, delete permanently,
/// empty), revisions, preview links and slug checks. Each
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
        posts.MapPost("/{id:int}/restore", (int id, IPostAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.RestoreAsync(id, ct)));
        posts.MapDelete("/{id:int}/permanent", DeletePostPermanentlyAsync);
        posts.MapPost("/empty-trash", async (IPostAdminService service, CancellationToken ct) =>
            TypedResults.Ok(new EmptyTrashResponse(await service.EmptyTrashAsync(ct))));
        posts.MapGet("/{id:int}/revisions", GetRevisionsAsync);
        posts.MapGet("/{id:int}/revisions/{revisionId:int}", GetRevisionAsync);
        posts.MapGet("/{id:int}/preview-tokens", GetPreviewLinksAsync);
        posts.MapPost("/{id:int}/preview-token", CreatePreviewLinkAsync);
        posts.MapDelete("/{id:int}/preview-tokens/{linkId:int}", RevokePreviewLinkAsync);
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

    /// <summary>Deletes a post in the trash for good (design 6.8); 404 unless the post is in the trash.</summary>
    private static async Task<Results<NoContent, NotFound>> DeletePostPermanentlyAsync(int id, IPostAdminService service,
        CancellationToken cancellationToken)
    {
        return await service.DeletePermanentlyAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Lists a post's revisions, newest first (A13).</summary>
    private static async Task<Results<Ok<IReadOnlyList<PostRevisionSummaryDto>>, NotFound>> GetRevisionsAsync(int id,
        IPostAdminService service, CancellationToken cancellationToken)
    {
        return await service.GetRevisionsAsync(id, cancellationToken) is { } revisions
            ? TypedResults.Ok(revisions)
            : TypedResults.NotFound();
    }

    /// <summary>Loads one revision with its content, for comparing and restoring.</summary>
    private static async Task<Results<Ok<PostRevisionDto>, NotFound>> GetRevisionAsync(int id, int revisionId,
        IPostAdminService service, CancellationToken cancellationToken)
    {
        return await service.GetRevisionAsync(id, revisionId, cancellationToken) is { } revision
            ? TypedResults.Ok(revision)
            : TypedResults.NotFound();
    }

    /// <summary>Lists a post's preview links that haven't expired (A14).</summary>
    private static async Task<Results<Ok<IReadOnlyList<PreviewLinkDto>>, NotFound>> GetPreviewLinksAsync(int id,
        IPreviewLinkService service, CancellationToken cancellationToken)
    {
        return await service.GetLinksAsync(id, cancellationToken) is { } links
            ? TypedResults.Ok(links)
            : TypedResults.NotFound();
    }

    /// <summary>Creates a preview link (A14); the body is optional and defaults to a 7-day link.</summary>
    private static async Task<Results<Ok<PreviewLinkDto>, NotFound, ValidationProblem>> CreatePreviewLinkAsync(int id,
        CreatePreviewLinkRequest? request, IPreviewLinkService service, IValidator<CreatePreviewLinkRequest> validator,
        CancellationToken cancellationToken)
    {
        request ??= new CreatePreviewLinkRequest();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        return await service.CreateAsync(id, request, cancellationToken) is { } link
            ? TypedResults.Ok(link)
            : TypedResults.NotFound();
    }

    /// <summary>Revokes a preview link; its URL returns 404 from then on.</summary>
    private static async Task<Results<NoContent, NotFound>> RevokePreviewLinkAsync(int id, int linkId, IPreviewLinkService service,
        CancellationToken cancellationToken)
    {
        return await service.RevokeAsync(id, linkId, cancellationToken)
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
