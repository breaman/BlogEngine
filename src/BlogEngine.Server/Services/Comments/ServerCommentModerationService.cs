using BlogEngine.Data.Common;
using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Server implementation of <see cref="ICommentModerationService"/> (design 8.4, T3.7), working on
/// <see cref="ApplicationDbContext"/> directly. Used while prerendering <c>/admin/comments</c> and behind the
/// <c>/api/admin/comments</c> and <c>/api/admin/comment-blocks</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public cache.</b> The post page caches approved comments per post. Any change that adds a comment to, or removes
/// one from, the approved set evicts that post's entry through <see cref="CacheInvalidator.CommentsChangedAsync"/>.
/// </para>
/// <para>
/// <b>Deleting</b> is permanent (design 6.5). Replies can't cascade from their parent in SQL Server (a self-reference),
/// so a deleted comment takes its replies with it explicitly.
/// </para>
/// <para>
/// <b>Trash.</b> Comments on trashed posts are hidden by the query filter, so they drop out of the queue until the post
/// is restored, and emptying the trash removes them with the post.
/// </para>
/// </remarks>
public sealed class ServerCommentModerationService(
    ApplicationDbContext dbContext,
    IUserService userService,
    IValidator<CommentBlockRequest> blockValidator,
    IValidator<CommentBulkRequest> bulkValidator,
    IValidator<CommentReplyRequest> replyValidator,
    AuthorCommentWriter authorComments,
    CacheInvalidator cacheInvalidator,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<ServerCommentModerationService> logger) : ICommentModerationService
{
    /// <summary>"Empty spam" deletes spam older than this (design 8.4).</summary>
    public static readonly TimeSpan SpamRetention = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public async Task<PagedResult<CommentDto>> GetCommentsAsync(CommentListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, CommentListQuery.MaxPageSize);

        var comments = dbContext.Comments.AsNoTracking().Where(c => c.Status == query.Status);
        var totalCount = await comments.CountAsync(cancellationToken);
        var items = await Project(comments.OrderByDescending(c => c.CreatedOn).ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<CommentDto>([.. items.Select(WithPostPath)], totalCount, page, pageSize);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs on its own <see cref="ApplicationDbContext"/>: the nav badge asks for the counts from the admin layout, which
    /// prerenders at the same time as the page, and the page's queries use the request's context.
    /// </remarks>
    public async Task<CommentStatusCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var counts = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Comments
            .AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        return new CommentStatusCounts
        {
            Pending = counts.GetValueOrDefault(CommentStatus.Pending),
            Approved = counts.GetValueOrDefault(CommentStatus.Approved),
            Rejected = counts.GetValueOrDefault(CommentStatus.Rejected),
            Spam = counts.GetValueOrDefault(CommentStatus.Spam)
        };
    }

    /// <inheritdoc />
    public async Task<bool> ModerateAsync(int id, CommentModerationAction action, CancellationToken cancellationToken = default)
    {
        return await ApplyAsync([id], action, cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<CommentReplyResult> ReplyAsync(int id, CommentReplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await replyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new CommentReplyInvalid(validation.ToDictionary().AsReadOnly());
        }

        var target = await dbContext.Comments.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (target is null)
        {
            return CommentReplyResult.NotFound;
        }

        // Threads are one level deep (design 6.5), so answering a reply files the answer under its top-level comment.
        var topLevel = target.ParentCommentId is { } parentId
            ? await dbContext.Comments.SingleOrDefaultAsync(c => c.Id == parentId, cancellationToken) ?? target
            : target;

        // Answering a comment vouches for it (design 8.4): approve it, and its top-level comment, or the reply would
        // hang under a thread readers can't see.
        var approving = new[] { topLevel, target }.Distinct().Where(c => c.Status != CommentStatus.Approved).ToList();
        SetStatus(approving, CommentStatus.Approved);

        var reply = await authorComments.AddAsync(target.PostId, topLevel.Id, request.Body, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.CommentsChangedAsync([target.PostId]);

        CommentLog.AuthorCommentPosted(logger, reply.Id, reply.PostId, userService.UserId, reply.ParentCommentId);

        var row = await Project(dbContext.Comments.AsNoTracking().Where(c => c.Id == reply.Id)).SingleAsync(cancellationToken);
        return new CommentReplied(WithPostPath(row), [.. approving.Select(c => c.Id)]);
    }

    /// <inheritdoc />
    public async Task<int> ModerateManyAsync(CommentBulkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await bulkValidator.ValidateAndThrowAsync(request, cancellationToken);

        return await ApplyAsync(request.Ids, request.Action, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommenterBlocked?> BlockCommenterAsync(int id, CancellationToken cancellationToken = default)
    {
        var comment = await dbContext.Comments.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.AuthorEmail, c.IpHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (comment is null)
        {
            return null;
        }

        var email = CommentBlockValues.Normalize(CommentBlockKind.Email, comment.AuthorEmail);
        var ipHash = CommentBlockValues.Normalize(CommentBlockKind.IpHash, comment.IpHash);
        var wanted = new List<(CommentBlockKind Kind, string Value)>();
        if (email.Length > 0)
        {
            wanted.Add((CommentBlockKind.Email, email));
        }

        if (ipHash.Length > 0)
        {
            wanted.Add((CommentBlockKind.IpHash, ipHash));
        }

        var existing = await dbContext.CommentBlocks
            .Where(b => (b.Kind == CommentBlockKind.Email && b.Value == email) || (b.Kind == CommentBlockKind.IpHash && b.Value == ipHash))
            .ToListAsync(cancellationToken);
        var added = wanted
            .Where(w => !existing.Any(b => b.Kind == w.Kind && b.Value == w.Value))
            .Select(w => new CommentBlock { Kind = w.Kind, Value = w.Value, Note = $"Blocked from comment {id}" })
            .ToList();
        dbContext.CommentBlocks.AddRange(added);

        // Everything from this commenter goes to spam, except the author's own replies.
        var theirs = await dbContext.Comments
            .Where(c => !c.IsAuthorReply && c.Status != CommentStatus.Spam
                && (c.AuthorEmail == comment.AuthorEmail || (ipHash.Length > 0 && c.IpHash == ipHash)))
            .ToListAsync(cancellationToken);
        var changedPosts = SetStatus(theirs, CommentStatus.Spam);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.CommentsChangedAsync(changedPosts);

        CommentLog.CommenterBlocked(logger, id, userService.UserId, added.Count, theirs.Count);
        return new CommenterBlocked([.. existing.Concat(added).OrderBy(b => b.Kind).Select(ToDto)], theirs.Count);
    }

    /// <inheritdoc />
    public async Task<int> EmptySpamAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow() - SpamRetention;
        var spam = await dbContext.Comments
            .Where(c => c.Status == CommentStatus.Spam && c.CreatedOn < cutoff)
            .ToListAsync(cancellationToken);
        if (spam.Count == 0)
        {
            return 0;
        }

        // Replies go too, since the self-reference doesn't cascade. One SaveChanges deletes everything in a single
        // transaction, replies before their parents.
        var ids = spam.Select(c => c.Id).ToList();
        var replies = await dbContext.Comments
            .Where(c => c.ParentCommentId != null && ids.Contains(c.ParentCommentId.Value) && !ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        dbContext.Comments.RemoveRange(spam);
        dbContext.Comments.RemoveRange(replies);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Spam is never shown, but an approved reply to it was.
        await cacheInvalidator.CommentsChangedAsync(replies.Where(r => r.Status == CommentStatus.Approved).Select(r => r.PostId));

        var deleted = spam.Count + replies.Count;
        CommentLog.SpamEmptied(logger, deleted, cutoff, userService.UserId);
        return deleted;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentBlockDto>> GetBlocksAsync(CancellationToken cancellationToken = default)
    {
        var blocks = await dbContext.CommentBlocks
            .AsNoTracking()
            .OrderBy(b => b.Kind)
            .ThenBy(b => b.Value)
            .ToListAsync(cancellationToken);

        return [.. blocks.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<CommentBlockSaveResult> AddBlockAsync(CommentBlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await blockValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new CommentBlockInvalid(validation.ToDictionary().AsReadOnly());
        }

        var value = CommentBlockValues.Normalize(request.Kind, request.Value);
        if (await FindBlockAsync(request.Kind, value, cancellationToken) is { } existing)
        {
            return new CommentBlockSaved(ToDto(existing));
        }

        var block = new CommentBlock
        {
            Kind = request.Kind,
            Value = value,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        dbContext.CommentBlocks.Add(block);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another request added the same block in between; the unique index kept one, so return it.
            dbContext.Entry(block).State = EntityState.Detached;
            var winner = await FindBlockAsync(request.Kind, value, cancellationToken)
                ?? throw new InvalidOperationException("A duplicate comment block was reported but can't be found.", ex);
            return new CommentBlockSaved(ToDto(winner));
        }

        logger.LogInformation("Comment block {BlockId} added: {Kind}", block.Id, block.Kind);
        return new CommentBlockSaved(ToDto(block));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBlockAsync(int id, CancellationToken cancellationToken = default)
    {
        var block = await dbContext.CommentBlocks.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (block is null)
        {
            return false;
        }

        dbContext.CommentBlocks.Remove(block);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Comment block {BlockId} removed: {Kind}", id, block.Kind);
        return true;
    }

    /// <summary>Applies <paramref name="action"/> to the comments with these ids and returns how many it touched.</summary>
    private async Task<int> ApplyAsync(IReadOnlyCollection<int> ids, CommentModerationAction action, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var comments = await dbContext.Comments.Where(c => distinct.Contains(c.Id)).ToListAsync(cancellationToken);
        if (comments.Count == 0)
        {
            return 0;
        }

        IReadOnlyCollection<int> changedPosts;
        if (action == CommentModerationAction.Delete)
        {
            var replies = await dbContext.Comments
                .Where(c => c.ParentCommentId != null && distinct.Contains(c.ParentCommentId.Value) && !distinct.Contains(c.Id))
                .ToListAsync(cancellationToken);
            var removed = comments.Concat(replies).ToList();
            dbContext.Comments.RemoveRange(removed);
            changedPosts = [.. removed.Where(c => c.Status == CommentStatus.Approved).Select(c => c.PostId).Distinct()];

            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var comment in comments)
            {
                CommentLog.CommentDeleted(logger, comment.Id, comment.PostId, comment.Status, userService.UserId);
            }
        }
        else
        {
            var status = action switch
            {
                CommentModerationAction.Approve => CommentStatus.Approved,
                CommentModerationAction.Reject => CommentStatus.Rejected,
                CommentModerationAction.Spam => CommentStatus.Spam,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown moderation action.")
            };

            changedPosts = SetStatus(comments, status);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await cacheInvalidator.CommentsChangedAsync(changedPosts);
        return comments.Count;
    }

    /// <summary>
    /// Moves the comments to <paramref name="status"/>, stamps and logs the moderation, and returns the posts whose
    /// approved comments changed.
    /// </summary>
    private IReadOnlyCollection<int> SetStatus(IEnumerable<Comment> comments, CommentStatus status)
    {
        var now = timeProvider.GetUtcNow();
        var moderatorId = userService.UserId;
        var changedPosts = new HashSet<int>();

        foreach (var comment in comments)
        {
            var previous = comment.Status;
            if (previous != status && (previous == CommentStatus.Approved || status == CommentStatus.Approved))
            {
                changedPosts.Add(comment.PostId);
            }

            comment.Status = status;
            comment.ModeratedOn = now;
            comment.ModeratedBy = moderatorId;
            CommentLog.CommentModerated(logger, comment.Id, comment.PostId, previous, status, moderatorId);
        }

        return changedPosts;
    }

    private Task<CommentBlock?> FindBlockAsync(CommentBlockKind kind, string value, CancellationToken cancellationToken)
    {
        return dbContext.CommentBlocks.AsNoTracking().SingleOrDefaultAsync(b => b.Kind == kind && b.Value == value, cancellationToken);
    }

    /// <summary>Projects comments into moderation rows (the public path is filled in by <see cref="WithPostPath"/>).</summary>
    internal static IQueryable<CommentRow> Project(IQueryable<Comment> comments)
    {
        return comments.Select(c => new CommentRow(
            new CommentDto
            {
                Id = c.Id,
                PostId = c.PostId,
                PostTitle = c.Post.Title,
                ParentCommentId = c.ParentCommentId,
                AuthorName = c.AuthorName,
                AuthorEmail = c.AuthorEmail,
                AuthorUrl = c.AuthorUrl,
                BodyHtml = c.BodyHtml,
                Status = c.Status,
                IsAuthorReply = c.IsAuthorReply,
                SpamScore = c.SpamScore,
                SpamReasons = c.SpamReasons,
                CreatedOn = c.CreatedOn,
                ModeratedOn = c.ModeratedOn
            },
            c.Post.Status,
            c.Post.PublishedDateLocal,
            c.Post.Slug));
    }

    /// <summary>Sets <see cref="CommentDto.PostPath"/> when the post is published.</summary>
    internal static CommentDto WithPostPath(CommentRow row)
    {
        row.Comment.PostPath = row.PostStatus == PostStatus.Published && row.PublishedDateLocal is { } date && row.Slug.Length > 0
            ? PostPaths.Post(date, row.Slug)
            : null;
        return row.Comment;
    }

    private static CommentBlockDto ToDto(CommentBlock block)
    {
        return new CommentBlockDto { Id = block.Id, Kind = block.Kind, Value = block.Value, Note = block.Note, CreatedOn = block.CreatedOn };
    }

    /// <summary>A moderation row plus the post fields its public path is built from.</summary>
    internal sealed record CommentRow(CommentDto Comment, PostStatus PostStatus, DateOnly? PublishedDateLocal, string Slug);
}
