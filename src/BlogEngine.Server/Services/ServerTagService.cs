using BlogEngine.Data.Common;
using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="ITagService"/>, querying <see cref="ApplicationDbContext"/> directly: editor
/// autocomplete and tag management (design 6.4, O3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Posts in the trash keep their tags.</b> Merging retags them too, and a tag they use can't be deleted, so restoring
/// a post never loses a tag. The counts shown to the author list them separately.
/// </para>
/// <para>
/// <b>Open editors.</b> Renaming or merging changes the tag names a post shows without saving the post. An editor opened
/// before that would send the old name back and recreate the old tag, so the affected posts' row versions are bumped
/// (with <c>ExecuteUpdate</c>, which leaves their modified stamps and pending autosaves alone): the editor's next save
/// reports a conflict and offers to reload.
/// </para>
/// <para>
/// Renames and merges run in one transaction, through the execution strategy so a retrying strategy can replay them. The
/// unique indexes on <see cref="Tag.NormalizedName"/> and <see cref="Tag.Slug"/> are the final guard against a race with
/// another save; losing one is reported as a <see cref="TagConflict"/>.
/// </para>
/// </remarks>
public sealed class ServerTagService(
    ApplicationDbContext dbContext,
    IValidator<UpdateTagRequest> updateValidator,
    CacheInvalidator cacheInvalidator,
    IUserService userService,
    ILogger<ServerTagService> logger) : ITagService
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagAdminDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // PostTags' query filter hides links to trashed posts, so PostCount counts the posts outside the trash.
        var tags = await dbContext.Tags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TagAdminDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                Description = t.Description,
                PostCount = t.PostTags.Count()
            })
            .ToListAsync(cancellationToken);

        var trashed = await TrashedPostCountsAsync(tagId: null, cancellationToken);
        foreach (var tag in tags)
        {
            tag.TrashedPostCount = trashed.GetValueOrDefault(tag.Id);
        }

        return tags;
    }

    /// <inheritdoc />
    public async Task<TagResult> UpdateAsync(int id, UpdateTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new TagInvalid(validation.ToDictionary().AsReadOnly());
        }

        var name = TagNormalizer.Normalize(request.Name);
        var requestedSlug = request.Slug?.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        return await InTransactionAsync(async ct =>
        {
            var tag = await dbContext.Tags.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (tag is null)
            {
                return TagResult.NotFound;
            }

            if (await dbContext.Tags.AnyAsync(t => t.Id != id && t.NormalizedName == name.NormalizedName, ct))
            {
                return new TagConflict($"Another tag is already named '{name.Name}'. Merge this tag into it instead.");
            }

            string slug;
            if (string.IsNullOrEmpty(requestedSlug))
            {
                // Derived from the name, keeping the current slug when it already is that (possibly with a -2 suffix), so
                // fixing the casing of a name doesn't move its URL.
                var derived = TagNormalizer.ToSlug(name.Name);
                slug = IsDerivedSlug(tag.Slug, derived) ? tag.Slug : await UniqueSlugAsync(derived, id, ct);
            }
            else if (requestedSlug != tag.Slug && await dbContext.Tags.AnyAsync(t => t.Id != id && t.Slug == requestedSlug, ct))
            {
                return TagResult.Invalid(nameof(UpdateTagRequest.Slug), $"Another tag already uses the slug '{requestedSlug}'.");
            }
            else
            {
                slug = requestedSlug;
            }

            var (oldName, oldSlug) = (tag.Name, tag.Slug);
            var nameChanged = !string.Equals(oldName, name.Name, StringComparison.Ordinal);
            var slugChanged = !string.Equals(oldSlug, slug, StringComparison.Ordinal);
            var descriptionChanged = !string.Equals(tag.Description, description, StringComparison.Ordinal);

            tag.Name = name.Name;
            tag.NormalizedName = name.NormalizedName;
            tag.Slug = slug;
            tag.Description = description;

            if (slugChanged)
            {
                await AddTagRedirectsAsync(oldSlug, slug, ct);
            }

            await dbContext.SaveChangesAsync(ct);
            if (nameChanged)
            {
                await BumpPostVersionsAsync(await PostIdsWithTagAsync(id, ct), ct);
            }

            return new Updated(tag.Id, oldName, oldSlug, nameChanged || slugChanged || descriptionChanged);
        }, async result =>
        {
            if (result is Updated updated)
            {
                if (updated.ChangesPublicPages)
                {
                    await cacheInvalidator.TagsChangedAsync();
                }

                var tag = await LoadAsync(updated.TagId, cancellationToken);
                TagLog.TagUpdated(logger, tag.Id, updated.OldName, updated.OldSlug, tag.Name, tag.Slug, userService.UserId);
                return new TagSaved(tag);
            }

            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagResult> MergeAsync(int id, int targetId, CancellationToken cancellationToken = default)
    {
        if (id == targetId)
        {
            return TagResult.Invalid(string.Empty, "A tag can't be merged into itself.");
        }

        return await InTransactionAsync(async ct =>
        {
            var tags = await dbContext.Tags.Where(t => t.Id == id || t.Id == targetId).ToListAsync(ct);
            if (tags.Find(t => t.Id == id) is not { } source || tags.Find(t => t.Id == targetId) is not { } target)
            {
                return TagResult.NotFound;
            }

            // The target is added to every post with the source that doesn't have it already; deleting the source then
            // removes its links through the database cascade.
            var postIds = await PostIdsWithTagAsync(id, ct);
            var alreadyTagged = await dbContext.PostTags
                .IgnoreQueryFilters([QueryFilters.SoftDelete])
                .Where(pt => pt.TagId == targetId && postIds.Contains(pt.PostId))
                .Select(pt => pt.PostId)
                .ToListAsync(ct);
            foreach (var postId in postIds.Except(alreadyTagged))
            {
                dbContext.PostTags.Add(new PostTag { PostId = postId, TagId = targetId });
            }

            dbContext.Tags.Remove(source);
            await AddTagRedirectsAsync(source.Slug, target.Slug, ct);

            await dbContext.SaveChangesAsync(ct);
            await BumpPostVersionsAsync(postIds, ct);

            return new Merged(source.Id, source.Slug, target.Id, target.Slug, postIds.Count);
        }, async result =>
        {
            if (result is Merged merged)
            {
                await cacheInvalidator.TagsChangedAsync();
                TagLog.TagMerged(logger, merged.TagId, merged.Slug, merged.TargetId, merged.TargetSlug, merged.PostCount, userService.UserId);
                return new TagSaved(await LoadAsync(merged.TargetId, cancellationToken));
            }

            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var tag = await dbContext.Tags.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag is null)
        {
            return TagResult.NotFound;
        }

        var postCount = await dbContext.PostTags.CountAsync(pt => pt.TagId == id, cancellationToken);
        var trashedCount = (await TrashedPostCountsAsync(id, cancellationToken)).GetValueOrDefault(id);
        if (postCount + trashedCount > 0)
        {
            return new TagConflict(InUseMessage(tag.Name, postCount, trashedCount));
        }

        // A tag with no posts has no public page, so there is nothing to evict. Redirects that led to its page (from tags
        // merged into it) would now lead nowhere, so they go too.
        dbContext.Tags.Remove(tag);
        await dbContext.SaveChangesAsync(cancellationToken);

        string[] paths = [TagPaths.Tag(tag.Slug), TagPaths.Feed(tag.Slug)];
        await dbContext.Redirects.Where(r => paths.Contains(r.ToPath)).ExecuteDeleteAsync(cancellationToken);

        TagLog.TagDeleted(logger, tag.Id, tag.Slug, userService.UserId);
        return TagResult.Deleted;
    }

    /// <summary>Why a tag can't be deleted, naming the posts in and out of the trash that use it.</summary>
    private static string InUseMessage(string name, int postCount, int trashedCount)
    {
        var uses = (postCount, trashedCount) switch
        {
            (> 0, > 0) => $"{Posts(postCount)} and {Posts(trashedCount)} in the trash",
            (> 0, _) => Posts(postCount),
            _ => $"{Posts(trashedCount)} in the trash"
        };

        return $"'{name}' is used by {uses}. Merge it into another tag, or remove it from those posts first.";

        static string Posts(int count) => count == 1 ? "1 post" : $"{count} posts";
    }

    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction through the execution strategy (so a retrying strategy can replay
    /// it from a clean change tracker), then <paramref name="afterCommit"/> once it has committed. A lost unique-index race
    /// becomes a <see cref="TagConflict"/>.
    /// </summary>
    private async Task<TagResult> InTransactionAsync(Func<CancellationToken, Task<TagResult>> operation,
        Func<TagResult, Task<TagResult>> afterCommit, CancellationToken cancellationToken)
    {
        TagResult result;
        try
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(async ct =>
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
                var outcome = await operation(ct);
                await transaction.CommitAsync(ct);
                return outcome;
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            dbContext.ChangeTracker.Clear();
            logger.LogInformation(ex, "A tag change lost a unique-index race.");
            return new TagConflict("Another tag was saved with the same name or slug at the same time. Reload and try again.");
        }

        return await afterCommit(result);
    }

    /// <summary>
    /// Redirects the old tag page and feed to the new ones (design 6.7); <see cref="RedirectWriter"/> repoints redirects
    /// that led to the old page, so every old URL reaches the new one in one hop.
    /// </summary>
    private async Task AddTagRedirectsAsync(string oldSlug, string newSlug, CancellationToken cancellationToken)
    {
        await RedirectWriter.AddAsync(dbContext, TagPaths.Tag(oldSlug), TagPaths.Tag(newSlug), cancellationToken);
        await RedirectWriter.AddAsync(dbContext, TagPaths.Feed(oldSlug), TagPaths.Feed(newSlug), cancellationToken);
    }

    /// <summary>Ids of every post with the tag, including posts in the trash.</summary>
    private Task<List<int>> PostIdsWithTagAsync(int tagId, CancellationToken cancellationToken)
    {
        return dbContext.PostTags
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(pt => pt.TagId == tagId)
            .Select(pt => pt.PostId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Changes the row version of the posts without touching anything else, so open editors see a conflict.</summary>
    private async Task BumpPostVersionsAsync(IReadOnlyCollection<int> postIds, CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return;
        }

        await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => postIds.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Title, p => p.Title), cancellationToken);
    }

    /// <summary>Number of posts in the trash per tag, for one tag or all of them.</summary>
    private async Task<Dictionary<int, int>> TrashedPostCountsAsync(int? tagId, CancellationToken cancellationToken)
    {
        return await dbContext.PostTags
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(pt => pt.Post.IsDeleted && (tagId == null || pt.TagId == tagId))
            .GroupBy(pt => pt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, cancellationToken);
    }

    /// <summary>Loads one tag with its counts, as tag management shows it.</summary>
    private async Task<TagAdminDto> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var tag = await dbContext.Tags
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TagAdminDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                Description = t.Description,
                PostCount = t.PostTags.Count()
            })
            .SingleAsync(cancellationToken);
        tag.TrashedPostCount = (await TrashedPostCountsAsync(id, cancellationToken)).GetValueOrDefault(id);

        return tag;
    }

    /// <summary>The first free variant of <paramref name="slug"/> (<c>-2</c>, <c>-3</c>, …) among the other tags.</summary>
    private async Task<string> UniqueSlugAsync(string slug, int excludeTagId, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = await dbContext.Tags
            .Where(t => t.Id != excludeTagId && (t.Slug == slug || t.Slug.StartsWith(prefix)))
            .Select(t => t.Slug)
            .ToListAsync(cancellationToken);

        var takenSet = taken.ToHashSet(StringComparer.Ordinal);
        return SlugGenerator.MakeUnique(slug, takenSet.Contains, FieldLengths.TagSlug);
    }

    /// <summary>Whether <paramref name="slug"/> is <paramref name="derived"/>, possibly with a collision suffix.</summary>
    private static bool IsDerivedSlug(string slug, string derived)
    {
        return slug == derived
            || (slug.StartsWith(derived + "-", StringComparison.Ordinal)
                && int.TryParse(slug.AsSpan(derived.Length + 1), out var number)
                && number >= 2);
    }

    /// <summary>Internal outcome of a committed rename, turned into <see cref="TagSaved"/> after the commit.</summary>
    private sealed record Updated(int TagId, string OldName, string OldSlug, bool ChangesPublicPages) : TagResult;

    /// <summary>Internal outcome of a committed merge, turned into <see cref="TagSaved"/> after the commit.</summary>
    private sealed record Merged(int TagId, string Slug, int TargetId, string TargetSlug, int PostCount) : TagResult;
}
