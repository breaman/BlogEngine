using BlogEngine.Data.Models;
using BlogEngine.Data.Queries;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Site search (design 15, P9): a <c>LIKE</c> match of every search word against the title, summary and Markdown of the
/// visible posts, with posts whose title contains all the words ranked first, then newest first.
/// </summary>
/// <remarks>
/// <para>
/// The database only finds the matching ids (the Markdown isn't part of the cached snapshot); the results are then
/// taken from <see cref="PublicPostQueries.GetIndexAsync"/>, so they show the same summaries as every other list. At
/// personal-blog scale (hundreds of posts) a <c>LIKE</c> scan is fast enough; full-text search is planned for later
/// (T5.9). EF Core escapes <c>%</c>, <c>_</c> and <c>[</c> in the words, so they match literally, and matching is
/// case-insensitive through the database's default collation.
/// </para>
/// <para>
/// Results aren't cached: the queries are unbounded, so caching them would let anyone fill the cache. The page is rate
/// limited instead (<see cref="SearchRateLimiting"/>).
/// </para>
/// </remarks>
public sealed class PublicSearchQueries(PublicPostQueries queries, IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    /// <summary>Page <paramref name="page"/> of the posts matching <paramref name="query"/>, best matches first.</summary>
    public async Task<PublicPostPage> SearchAsync(SearchQuery query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matchingIds = await FindMatchingIdsAsync(query, cancellationToken);
        var index = await queries.GetIndexAsync(cancellationToken);

        // A post published or removed a moment ago may be in only one of the two; the snapshot decides what is shown.
        PublicPostSummary[] ranked =
        [
            .. index.Posts
                .Where(p => matchingIds.Contains(p.Id))
                .Select((post, position) => (Post: post, Position: position, InTitle: query.MatchesAllIn(post.Title)))
                .OrderByDescending(r => r.InTitle)
                .ThenBy(r => r.Position)
                .Select(r => r.Post)
        ];

        return PublicPostPage.Create(ranked, page, pageSize);
    }

    /// <summary>The ids of the visible posts containing every term in their title, summary or Markdown.</summary>
    private async Task<HashSet<int>> FindMatchingIdsAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var posts = dbContext.Posts.AsNoTracking().VisibleToPublic(timeProvider);
        foreach (var term in query.Terms)
        {
            posts = posts.Where(p => p.Title.Contains(term) || p.Summary.Contains(term) || p.ContentMarkdown.Contains(term));
        }

        return [.. await posts.Select(p => p.Id).ToListAsync(cancellationToken)];
    }
}
