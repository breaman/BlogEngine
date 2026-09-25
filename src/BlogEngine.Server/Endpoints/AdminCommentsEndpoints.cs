using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for comment moderation and the blocklist (design 7.4, 8.4, T3.7, T3.9). Handlers delegate to
/// <see cref="ICommentModerationService"/>.
/// </summary>
/// <remarks>
/// Besides the endpoints listed in the design, <c>GET /comments/counts</c> feeds the tab labels and nav badge,
/// <c>POST /comments/{id}/block</c> is "Block commenter", <c>POST /comments/empty-spam</c> is "Empty spam", and
/// <c>GET /comment-blocks</c> lists the blocklist.
/// </remarks>
public static class AdminCommentsEndpoints
{
    /// <summary>Maps the comment endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminCommentsEndpoints(this RouteGroupBuilder group)
    {
        var comments = group.MapGroup("/comments");

        comments.MapGet("/", GetCommentsAsync);
        comments.MapGet("/counts", async (ICommentModerationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetCountsAsync(ct)));
        comments.MapPost("/{id:int}/approve", (int id, ICommentModerationService service, CancellationToken ct) =>
            ModerateAsync(id, CommentModerationAction.Approve, service, ct));
        comments.MapPost("/{id:int}/reject", (int id, ICommentModerationService service, CancellationToken ct) =>
            ModerateAsync(id, CommentModerationAction.Reject, service, ct));
        comments.MapPost("/{id:int}/spam", (int id, ICommentModerationService service, CancellationToken ct) =>
            ModerateAsync(id, CommentModerationAction.Spam, service, ct));
        comments.MapDelete("/{id:int}", (int id, ICommentModerationService service, CancellationToken ct) =>
            ModerateAsync(id, CommentModerationAction.Delete, service, ct));
        comments.MapPost("/{id:int}/reply", ReplyAsync);
        comments.MapPost("/bulk", BulkAsync);
        comments.MapPost("/{id:int}/block", BlockCommenterAsync);
        comments.MapPost("/empty-spam", async (ICommentModerationService service, CancellationToken ct) =>
            TypedResults.Ok(new EmptySpamResponse(await service.EmptySpamAsync(ct))));

        var blocks = group.MapGroup("/comment-blocks");

        blocks.MapGet("/", async (ICommentModerationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetBlocksAsync(ct)));
        blocks.MapPost("/", AddBlockAsync);
        blocks.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, ICommentModerationService service, CancellationToken ct) =>
            await service.DeleteBlockAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());

        return group;
    }

    /// <summary>
    /// One moderation tab: <c>GET /comments?status=Pending&amp;page=&amp;pageSize=</c>. Every parameter is optional; an
    /// unknown status is a 400.
    /// </summary>
    private static async Task<Results<Ok<PagedResult<CommentDto>>, ValidationProblem>> GetCommentsAsync(string? status, int? page,
        int? pageSize, ICommentModerationService service, CancellationToken cancellationToken)
    {
        var parsed = CommentStatus.Pending;
        if (!string.IsNullOrWhiteSpace(status)
            && (!Enum.TryParse(status, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed)))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Unknown status '{status}'. Use one of {string.Join(", ", Enum.GetNames<CommentStatus>())}."]
            });
        }

        var query = new CommentListQuery
        {
            Status = parsed,
            Page = page ?? 1,
            PageSize = pageSize ?? CommentListQuery.DefaultPageSize
        };

        return TypedResults.Ok(await service.GetCommentsAsync(query, cancellationToken));
    }

    /// <summary>Approves, rejects, flags or deletes one comment: 204, or 404 if it doesn't exist.</summary>
    private static async Task<Results<NoContent, NotFound>> ModerateAsync(int id, CommentModerationAction action,
        ICommentModerationService service, CancellationToken cancellationToken)
    {
        return await service.ModerateAsync(id, action, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    /// <summary>
    /// The author's reply (design 8.4, C5): the published reply and the comments it approved, a 400 validation problem,
    /// or 404 when the comment replied to doesn't exist.
    /// </summary>
    private static async Task<Results<Ok<CommentReplied>, ValidationProblem, NotFound>> ReplyAsync(int id, CommentReplyRequest request,
        ICommentModerationService service, CancellationToken cancellationToken)
    {
        return await service.ReplyAsync(id, request, cancellationToken) switch
        {
            CommentReplied replied => TypedResults.Ok(replied),
            CommentReplyInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            CommentReplyNotFound => TypedResults.NotFound(),
            var other => throw new InvalidOperationException($"Unknown reply result {other.GetType().Name}.")
        };
    }

    /// <summary>Applies one action to several comments; returns how many changed.</summary>
    private static async Task<Results<Ok<BulkModerationResponse>, ValidationProblem>> BulkAsync(CommentBulkRequest request,
        ICommentModerationService service, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(new BulkModerationResponse(await service.ModerateManyAsync(request, cancellationToken)));
        }
        catch (ValidationException ex)
        {
            return TypedResults.ValidationProblem(ex.Errors
                .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.ToArray()));
        }
    }

    /// <summary>"Block commenter": the blocks now in place and how many comments went to spam, or 404.</summary>
    private static async Task<Results<Ok<CommenterBlocked>, NotFound>> BlockCommenterAsync(int id, ICommentModerationService service,
        CancellationToken cancellationToken)
    {
        return await service.BlockCommenterAsync(id, cancellationToken) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
    }

    /// <summary>Adds a blocklist entry; returns it, or a 400 validation problem.</summary>
    private static async Task<Results<Ok<CommentBlockDto>, ValidationProblem>> AddBlockAsync(CommentBlockRequest request,
        ICommentModerationService service, CancellationToken cancellationToken)
    {
        return await service.AddBlockAsync(request, cancellationToken) switch
        {
            CommentBlockSaved saved => TypedResults.Ok(saved.Block),
            CommentBlockInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Unknown block result {other.GetType().Name}.")
        };
    }
}
