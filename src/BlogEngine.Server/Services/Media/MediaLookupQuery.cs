using BlogEngine.Data.Models;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Markdown;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Loads the media metadata the Markdown renderer needs (design 9.4) for a set of public ids.
/// </summary>
public static class MediaLookupQuery
{
    /// <summary>Most ids looked up at once; a post referencing more than this is not realistic.</summary>
    public const int MaxIds = 500;

    /// <summary>The library items with these public ids; unknown ids are left out.</summary>
    public static async Task<MediaLookup> LoadAsync(ApplicationDbContext dbContext, IEnumerable<string> publicIds,
        CancellationToken cancellationToken)
    {
        var ids = publicIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxIds).ToList();
        if (ids.Count == 0)
        {
            return MediaLookup.Empty;
        }

        var items = await dbContext.MediaItems
            .AsNoTracking()
            .Where(m => ids.Contains(m.PublicId))
            .Select(m => new MediaLookupItem(m.Id, m.PublicId, m.FileName, m.Width, m.Height, m.Version, m.AltText))
            .ToListAsync(cancellationToken);

        return new MediaLookup(items);
    }
}
