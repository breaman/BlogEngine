using BlogEngine.Data.Models;
using BlogEngine.Data.Queries;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Accepts comments from the public comment form (design 8, C1, T3.3, T3.4): validates them, runs the
/// <see cref="SpamGuard"/>, renders and stores them, and logs the outcome.
/// </summary>
/// <remarks>
/// The rate limit is enforced earlier, by the ASP.NET Core rate limiter (<see cref="CommentRateLimiting"/>), so a
/// flood never reaches the database. Everything the commenter is told is deliberately vague: spam, discarded and
/// pending comments all report <see cref="CommentSubmitOutcome.AwaitingModeration"/>, so bots learn nothing.
/// </remarks>
public sealed class CommentSubmissionService(
    ApplicationDbContext dbContext,
    ISettingsService settingsService,
    IValidator<CommentSubmission> validator,
    SpamGuard spamGuard,
    CommentRenderer renderer,
    CommentFormTimestamp formTimestamp,
    CacheInvalidator cacheInvalidator,
    TimeProvider timeProvider,
    ILogger<CommentSubmissionService> logger)
{
    /// <summary>Submits a comment on <paramref name="postId"/>.</summary>
    /// <param name="postId">The post commented on.</param>
    /// <param name="submission">What the reader typed.</param>
    /// <param name="context">The request details the spam guard needs.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    public async Task<CommentSubmitResult> SubmitAsync(int postId, CommentSubmission submission, CommentSubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(context);

        var validation = await validator.ValidateAsync(submission, cancellationToken);
        if (!validation.IsValid)
        {
            return CommentSubmitResult.Invalid(validation.ToDictionary());
        }

        var now = timeProvider.GetUtcNow();
        var post = await dbContext.Posts
            .AsNoTracking()
            .VisibleToPublic(timeProvider)
            .Where(p => p.Id == postId)
            .Select(p => new { p.AllowComments, p.CommentsCloseOn })
            .SingleOrDefaultAsync(cancellationToken);
        if (post is null)
        {
            return CommentSubmitResult.NotFound;
        }

        var settings = await settingsService.GetAsync(cancellationToken);
        if (!settings.CommentsEnabled || !post.AllowComments || post.CommentsCloseOn <= now)
        {
            return CommentSubmitResult.Closed;
        }

        var email = submission.AuthorEmail.Trim().ToLowerInvariant();
        var blocks = await dbContext.CommentBlocks
            .AsNoTracking()
            .Select(b => new { b.Kind, b.Value })
            .ToListAsync(cancellationToken);
        var hasApprovedComment = settings.AutoApproveReturningCommenters
            && await dbContext.Comments.AnyAsync(c => c.AuthorEmail == email && c.Status == CommentStatus.Approved, cancellationToken);

        var verdict = spamGuard.Evaluate(new SpamCheck(
            submission,
            context.Honeypot,
            formTimestamp.Read(postId, context.FormToken),
            now,
            context.IpHash,
            CommentBlocklist.Create(blocks.Select(b => (b.Kind, b.Value))),
            settings.MaxCommentLinks,
            settings.RequireCommentApproval,
            settings.AutoApproveReturningCommenters,
            hasApprovedComment));

        if (verdict.Outcome == SpamOutcome.Discard)
        {
            CommentLog.CommentDiscarded(logger, postId, string.Join("; ", verdict.Reasons));
            return CommentSubmitResult.AwaitingModeration;
        }

        var reasons = verdict.Reasons.Count == 0 ? null : Truncate(string.Join("; ", verdict.Reasons), FieldLengths.SpamReasons);
        var comment = new Comment
        {
            PostId = postId,
            AuthorName = submission.AuthorName.Trim(),
            AuthorEmail = email,
            AuthorUrl = string.IsNullOrWhiteSpace(submission.AuthorUrl) ? null : submission.AuthorUrl.Trim(),
            BodyMarkdown = submission.Body.Trim(),
            BodyHtml = renderer.Render(submission.Body.Trim()),
            Status = verdict.Status,
            IpHash = context.IpHash,
            UserAgent = string.IsNullOrWhiteSpace(context.UserAgent) ? null : Truncate(context.UserAgent, FieldLengths.UserAgent),
            SpamScore = verdict.Score,
            SpamReasons = reasons
        };

        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        CommentLog.CommentSubmitted(logger, comment.Id, postId, comment.Status, comment.SpamScore, reasons ?? "none");

        if (comment.Status == CommentStatus.Approved)
        {
            await cacheInvalidator.CommentsChangedAsync([postId]);
            return CommentSubmitResult.Published;
        }

        return CommentSubmitResult.AwaitingModeration;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

/// <summary>The request details <see cref="CommentSubmissionService"/> needs besides the comment itself.</summary>
/// <param name="IpHash">The commenter's salted IP hash (<see cref="CommentIpHasher"/>).</param>
/// <param name="UserAgent">The browser's user agent, kept as moderation context.</param>
/// <param name="Honeypot">The hidden honeypot field; people leave it empty.</param>
/// <param name="FormToken">The signed render timestamp (<see cref="CommentFormTimestamp"/>).</param>
public sealed record CommentSubmissionContext(string IpHash, string? UserAgent, string? Honeypot, string? FormToken);

/// <summary>What the commenter is told.</summary>
public enum CommentSubmitOutcome
{
    /// <summary>"Your comment is awaiting moderation." Also used for spam and discarded submissions.</summary>
    AwaitingModeration,

    /// <summary>Approved straight away (moderation is off, or a returning commenter with auto-approve on).</summary>
    Published,

    /// <summary>Comments are closed on this post, or turned off site-wide.</summary>
    Closed,

    /// <summary>The post isn't visible.</summary>
    NotFound,

    /// <summary>The form has errors; nothing was stored.</summary>
    Invalid
}

/// <summary>The result of submitting a comment.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Errors">Validation messages keyed by property name, for <see cref="CommentSubmitOutcome.Invalid"/>.</param>
public sealed record CommentSubmitResult(CommentSubmitOutcome Outcome, IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>The comment is waiting for moderation (as far as the commenter knows).</summary>
    public static CommentSubmitResult AwaitingModeration { get; } = new(CommentSubmitOutcome.AwaitingModeration, new Dictionary<string, string[]>());

    /// <summary>The comment is live.</summary>
    public static CommentSubmitResult Published { get; } = new(CommentSubmitOutcome.Published, new Dictionary<string, string[]>());

    /// <summary>Comments are closed.</summary>
    public static CommentSubmitResult Closed { get; } = new(CommentSubmitOutcome.Closed, new Dictionary<string, string[]>());

    /// <summary>The post isn't visible.</summary>
    public static CommentSubmitResult NotFound { get; } = new(CommentSubmitOutcome.NotFound, new Dictionary<string, string[]>());

    /// <summary>The submission failed validation.</summary>
    public static CommentSubmitResult Invalid(IDictionary<string, string[]> errors)
    {
        return new CommentSubmitResult(CommentSubmitOutcome.Invalid, errors.AsReadOnly());
    }
}
