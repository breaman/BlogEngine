using BlogEngine.Data.Models;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="ITagService"/>, querying <see cref="ApplicationDbContext"/> directly.
/// </summary>
public sealed class ServerTagService(ApplicationDbContext dbContext) : ITagService
{
    /// <inheritdoc />
    /// <remarks>
    /// Matching uses <see cref="Tag.NormalizedName"/>, so it is case-insensitive under the same rules as
    /// duplicate detection (<c>c#</c> finds <c>C#</c>). Prefix matches rank first so typing a whole tag name
    /// puts that tag at the top even when a longer tag containing it is used more. Usage counts exclude
    /// trashed posts through the <c>PostTag</c> query filter.
    /// </remarks>
    public async Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var term = TagNormalizer.ToNormalizedName(search);

        var tags = dbContext.Tags.AsNoTracking();
        if (term.Length > 0)
        {
            tags = tags.Where(t => t.NormalizedName.Contains(term));
        }

        return await tags
            .Select(t => new
            {
                Tag = t,
                IsPrefix = term.Length > 0 && t.NormalizedName.StartsWith(term),
                PostCount = t.PostTags.Count()
            })
            .OrderByDescending(x => x.IsPrefix)
            .ThenByDescending(x => x.PostCount)
            .ThenBy(x => x.Tag.Name)
            .Take(ITagService.MaxSuggestions)
            .Select(x => new TagDto { Id = x.Tag.Id, Name = x.Tag.Name, Slug = x.Tag.Slug, PostCount = x.PostCount })
            .ToListAsync(cancellationToken);
    }
}
