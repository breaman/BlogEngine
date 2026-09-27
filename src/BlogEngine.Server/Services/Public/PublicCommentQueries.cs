using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using BlogEngine.Data.Models;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Loads the approved comments shown on a post page (design 11, 14.2, T3.6), cached in <see cref="HybridCache"/> per post
/// with the tag <see cref="PublicCacheTags.Comments"/> until moderation or a new approved comment evicts it.
/// </summary>
/// <remarks>
/// Like <see cref="PublicPostQueries"/>, keys carry <see cref="CacheInvalidator.CommentsGeneration"/> so a load racing an
/// eviction can't be stored as fresh, loads run in their own scope, and the class is a singleton. Only posts the page
/// already resolved as visible reach it, so its keys are bounded by the number of posts.
/// </remarks>
public sealed class PublicCommentQueries(HybridCache cache, CacheInvalidator invalidator, IServiceScopeFactory scopeFactory)
{
    /// <summary>Safety net for entries nothing evicts.</summary>
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };

    /// <summary>The approved comments of the post, oldest first.</summary>
    public async Task<PublicCommentList> GetApprovedAsync(int postId, CancellationToken cancellationToken = default)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"public-comments:{invalidator.CommentsGeneration}:{postId}");

        return await cache.GetOrCreateAsync(key, (Queries: this, PostId: postId),
            static (state, ct) => state.Queries.LoadAsync(state.PostId, ct),
            EntryOptions, [PublicCacheTags.Comments(postId)], cancellationToken);
    }

    /// <summary>The Gravatar-style id of an email: lowercase hex SHA-256 of the trimmed, lowercased address.</summary>
    public static string AvatarHash(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private async ValueTask<PublicCommentList> LoadAsync(int postId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await dbContext.Comments
            .AsNoTracking()
            .Where(c => c.PostId == postId && c.Status == CommentStatus.Approved)
            .OrderBy(c => c.CreatedOn)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.ParentCommentId, c.AuthorName, c.AuthorUrl, c.AuthorEmail, c.BodyHtml, c.CreatedOn, c.IsAuthorReply })
            .ToListAsync(cancellationToken);

        return rows.Count == 0
            ? PublicCommentList.Empty
            : new PublicCommentList(
            [
                .. rows.Select(r => new PublicComment(r.Id, r.ParentCommentId, r.AuthorName, r.AuthorUrl, r.BodyHtml,
                    r.CreatedOn ?? DateTimeOffset.MinValue, r.IsAuthorReply, AvatarHash(r.AuthorEmail)))
            ]);
    }
}