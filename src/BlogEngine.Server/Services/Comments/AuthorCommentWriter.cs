using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Builds comments written by the blog author (design 8.4, C5): replies from the moderation queue, and comments the
/// signed-in admin posts on the post page. They skip the spam guard, are approved straight away and carry
/// <see cref="Comment.IsAuthorReply"/>, which the post page shows as an "Author" badge.
/// </summary>
/// <remarks>
/// The name shown is the author name from the settings, falling back to the account's display name; the email is the
/// account's, so it never appears publicly but still drives the avatar like any other commenter's.
/// </remarks>
public sealed class AuthorCommentWriter(
    ApplicationDbContext dbContext,
    ISettingsService settingsService,
    IUserService userService,
    CommentRenderer renderer,
    CommentIpHasher ipHasher,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    /// <summary>Name used when neither the settings nor the account has one.</summary>
    public const string FallbackName = "Author";

    /// <summary>
    /// Adds an approved author comment to the context (the caller saves it).
    /// </summary>
    /// <param name="postId">The post commented on.</param>
    /// <param name="parentCommentId">The top-level comment this replies to, or <see langword="null"/> for a new thread.</param>
    /// <param name="body">The comment in the restricted comment Markdown dialect.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    public async Task<Comment> AddAsync(int postId, int? parentCommentId, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var settings = await settingsService.GetAsync(cancellationToken);
        var userId = userService.UserId;
        var account = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName, u.UserName })
            .SingleOrDefaultAsync(cancellationToken);

        var name = FirstNonBlank(settings.AuthorName, account?.DisplayName, account?.UserName) ?? FallbackName;
        var trimmed = body.Trim();
        var context = httpContextAccessor.HttpContext;

        var comment = new Comment
        {
            PostId = postId,
            ParentCommentId = parentCommentId,
            AuthorName = name,
            AuthorEmail = (account?.Email ?? string.Empty).Trim().ToLowerInvariant(),
            BodyMarkdown = trimmed,
            BodyHtml = renderer.Render(trimmed),
            Status = CommentStatus.Approved,
            IsAuthorReply = true,
            IpHash = ipHasher.Hash(context?.Connection.RemoteIpAddress),
            UserAgent = context?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
                ? agent[..Math.Min(agent.Length, FieldLengths.UserAgent)]
                : null,
            ModeratedOn = timeProvider.GetUtcNow(),
            ModeratedBy = userId
        };

        dbContext.Comments.Add(comment);
        return comment;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    }
}
