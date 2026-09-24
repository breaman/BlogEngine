using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Loads the post behind a private preview link, <c>/preview/{token}</c> (design 6.7, 7.1, A14), ignoring the public
/// visibility rule: that is the point of a preview. Nothing is cached, so a revoked link stops working at once.
/// </summary>
/// <remarks>
/// Each lookup uses its own scope, like <see cref="PublicPostQueries"/>, because during static SSR the layout
/// initializes alongside the page and a shared <see cref="ApplicationDbContext"/> can't serve both at once. The
/// preview token's query filter already hides links to posts in the trash.
/// </remarks>
public sealed class PreviewPostQuery(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// The post the token points at, as saved, or <see langword="null"/> if the token doesn't exist, has expired or its
    /// post is in the trash.
    /// </summary>
    public async Task<PreviewPost?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > FieldLengths.PreviewToken)
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = timeProvider.GetUtcNow();

        var row = await dbContext.PreviewTokens
            .AsNoTracking()
            .Where(t => t.Token == token && t.ExpiresOn > now)
            .Select(t => new
            {
                t.ExpiresOn,
                t.Post.Id,
                t.Post.Title,
                t.Post.ContentHtml,
                t.Post.HasCodeBlocks,
                t.Post.Status,
                t.Post.PublishedOn,
                t.Post.ReadingMinutes,
                Tags = t.Post.Tags.OrderBy(tag => tag.Name).Select(tag => new { tag.Id, tag.Name, tag.Slug }).ToList(),
                Cover = t.Post.CoverMedia == null
                    ? null
                    : new { t.Post.CoverMedia.PublicId, t.Post.CoverMedia.FileName, t.Post.CoverMedia.Version, t.Post.CoverMedia.Width, t.Post.CoverMedia.Height, t.Post.CoverMedia.AltText }
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new PreviewPost(row.Id, row.Title, row.ContentHtml, row.HasCodeBlocks,
            PostSchedule.IsLive(row.Status, row.PublishedOn, now),
            row.Status == PostStatus.Published && !PostSchedule.IsLive(row.Status, row.PublishedOn, now) ? row.PublishedOn : null,
            row.ReadingMinutes,
            [.. row.Tags.Select(t => new PublicTagLink(t.Id, t.Name, t.Slug))],
            row.Cover is { } cover
                ? new PublicImage(MediaPaths.Versioned(cover.PublicId, cover.FileName, cover.Version), cover.Width, cover.Height, cover.AltText)
                : null,
            row.ExpiresOn);
    }
}

/// <summary>A post as a preview link shows it.</summary>
/// <param name="Id">The post id.</param>
/// <param name="Title">The saved title.</param>
/// <param name="Html">The saved, sanitized content HTML.</param>
/// <param name="HasCodeBlocks">Whether the page needs the code highlighting script.</param>
/// <param name="IsLive">Whether the post is already public (the preview then shows what is saved, which is live).</param>
/// <param name="ScheduledFor">When a scheduled post goes live; <see langword="null"/> otherwise.</param>
/// <param name="ReadingMinutes">Estimated reading time.</param>
/// <param name="Tags">The post's tags.</param>
/// <param name="Cover">The cover image, if any.</param>
/// <param name="ExpiresOn">When the preview link stops working.</param>
public sealed record PreviewPost(
    int Id,
    string Title,
    string Html,
    bool HasCodeBlocks,
    bool IsLive,
    DateTimeOffset? ScheduledFor,
    int ReadingMinutes,
    IReadOnlyList<PublicTagLink> Tags,
    PublicImage? Cover,
    DateTimeOffset ExpiresOn);
