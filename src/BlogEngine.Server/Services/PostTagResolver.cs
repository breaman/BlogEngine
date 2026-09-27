using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Text;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Turns the tag names typed in the editor into <see cref="Tag"/> entities (design 6.4, A4).
/// </summary>
/// <remarks>
/// <para>
/// Names are matched on <see cref="Tag.NormalizedName"/>, so typing <c>c#</c> reuses an existing <c>C#</c>
/// tag with its original casing. Unknown names become new tags that are added to the context and so are
/// inserted in the same <c>SaveChanges</c> (one transaction) as the post.
/// </para>
/// <para>
/// This does not handle the race where two saves create the same new tag at once. The unique index on
/// <see cref="Tag.NormalizedName"/> makes the loser's save fail; <see cref="ServerPostAdminService"/> then
/// retries the whole save, and on the retry this finds the winner's tag and reuses it.
/// </para>
/// </remarks>
internal static class PostTagResolver
{
    /// <summary>
    /// Resolves <paramref name="names"/> to tracked tags, creating (but not saving) any that don't exist.
    /// Invalid names are skipped (the validator reports them before this runs) and duplicates collapse.
    /// </summary>
    public static async Task<List<Tag>> ResolveAsync(ApplicationDbContext dbContext, IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        var requested = names
            .Select(TagNormalizer.Normalize)
            .Where(n => n.IsValid)
            .DistinctBy(n => n.NormalizedName, StringComparer.Ordinal)
            .ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        var normalizedNames = requested.Select(n => n.NormalizedName).ToList();
        var existing = await dbContext.Tags
            .Where(t => normalizedNames.Contains(t.NormalizedName))
            .ToDictionaryAsync(t => t.NormalizedName, StringComparer.Ordinal, cancellationToken);

        var tags = new List<Tag>(requested.Count);
        var slugsClaimedHere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in requested)
        {
            if (!existing.TryGetValue(name.NormalizedName, out var tag))
            {
                tag = new Tag
                {
                    Name = name.Name,
                    NormalizedName = name.NormalizedName,
                    Slug = await UniqueSlugAsync(dbContext, TagNormalizer.ToSlug(name.Name), slugsClaimedHere, cancellationToken)
                };
                dbContext.Tags.Add(tag);
            }

            tags.Add(tag);
        }

        return tags;
    }

    /// <summary>
    /// The first free variant of <paramref name="slug"/>, counting slugs already in the database and those
    /// given to other new tags in this save.
    /// </summary>
    private static async Task<string> UniqueSlugAsync(ApplicationDbContext dbContext, string slug,
        HashSet<string> slugsClaimedHere, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = await dbContext.Tags
            .Where(t => t.Slug == slug || t.Slug.StartsWith(prefix))
            .Select(t => t.Slug)
            .ToListAsync(cancellationToken);
        taken.AddRange(slugsClaimedHere);

        var takenSet = taken.ToHashSet(StringComparer.Ordinal);
        var unique = SlugGenerator.MakeUnique(slug, takenSet.Contains, FieldLengths.TagSlug);
        slugsClaimedHere.Add(unique);

        return unique;
    }
}