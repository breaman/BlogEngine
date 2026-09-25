using BlogEngine.Data.Common;
using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;
using BlogEngine.Shared.Validation;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="IPostAdminService"/>, working on <see cref="ApplicationDbContext"/>
/// directly. Used while prerendering admin pages and behind the <c>/api/admin/posts</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Every save runs the same pipeline (design 6.2, 7.2): render and sanitize the HTML, count words, fill a
/// blank summary, derive the slug from the title until the first publish, compute the local publish date,
/// resolve tags and rebuild media usage. Everything, including new tags, revisions and redirects, is written
/// by a single <c>SaveChanges</c>, so it commits or fails as one transaction.
/// </para>
/// <para>
/// Concurrency: the client's <see cref="PostEditDto.RowVersion"/> is compared with the stored one before
/// anything changes, and EF Core's <c>rowversion</c> check catches a change that lands between the read and
/// the write. Either way the caller gets <see cref="PostConflict"/> rather than overwriting another tab.
/// </para>
/// <para>
/// Public caches: once a save that changes what readers can see has committed (anything touching a published
/// post, publishing, unpublishing, trashing a published post), <see cref="CacheInvalidator"/> evicts the
/// cached lists, pages and feeds, so the change is visible on the next request (T1.17). Draft-only saves and
/// autosaves never change public content, so they leave the caches alone.
/// </para>
/// <para>
/// Races on unique indexes (two saves creating the same tag, or claiming the same slug) are resolved by
/// clearing the context and running the whole operation again: the retry sees the winner's row and reuses
/// it or picks the next free slug.
/// </para>
/// <para>
/// Trash (design 6.8, O6): <see cref="DeleteAsync"/> soft-deletes through <c>SoftDeleteInterceptor</c>, the
/// <see cref="PostListStatus.Trash"/> tab lists those posts with the soft-delete filter switched off, and
/// <see cref="RestoreAsync"/> brings one back as a draft. Deleting a post that is already in the trash is let through
/// as a real delete, and the database cascades it to tag links, comments, revisions, media usage and preview links.
/// </para>
/// </remarks>
public sealed class ServerPostAdminService(
    ApplicationDbContext dbContext,
    ISettingsService settingsService,
    PostContentRenderer contentRenderer,
    TimeProvider timeProvider,
    IValidator<PostEditDto> postValidator,
    CacheInvalidator cacheInvalidator,
    IUserService userService,
    ILogger<ServerPostAdminService> logger) : IPostAdminService
{
    /// <summary>Autosave revisions kept per post; older ones are pruned (design 6.7).</summary>
    public const int AutosavesToKeep = 20;

    /// <summary>Attempts at a save that keeps losing unique-index races before giving up.</summary>
    private const int MaxSaveAttempts = 3;

    /// <summary>Slug used when a title has no characters a slug can use, such as a title in a non-Latin script.</summary>
    private const string FallbackSlug = "post";

    /// <inheritdoc />
    public async Task<PagedResult<PostSummaryDto>> GetPostsAsync(PostListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PostListQuery.MaxPageSize);

        // "Scheduled" is a published post whose date is still ahead (design 6.3), so the tabs split on the time. The
        // trash is the only tab that switches the soft-delete filter off, and it shows every status.
        var now = timeProvider.GetUtcNow();
        IQueryable<Post> posts = query.Status == PostListStatus.Trash
            ? dbContext.Posts.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(p => p.IsDeleted)
            : dbContext.Posts;
        posts = posts.AsNoTracking();
        posts = query.Status switch
        {
            PostListStatus.Draft => posts.Where(p => p.Status == PostStatus.Draft),
            PostListStatus.Published => posts.Where(p => p.Status == PostStatus.Published && p.PublishedOn <= now),
            PostListStatus.Scheduled => posts.Where(p => p.Status == PostStatus.Published && p.PublishedOn > now),
            _ => posts
        };

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            // Accept either the name as typed (any casing) or the tag's slug from a URL.
            var normalizedName = TagNormalizer.ToNormalizedName(query.Tag);
            var slug = query.Tag.Trim().ToLowerInvariant();
            posts = posts.Where(p => p.Tags.Any(t => t.NormalizedName == normalizedName || t.Slug == slug));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Case-insensitive through the database's default collation.
            var term = query.Search.Trim();
            posts = posts.Where(p => p.Title.Contains(term) || p.Slug.Contains(term) || p.Summary.Contains(term));
        }

        var totalCount = await posts.CountAsync(cancellationToken);

        // Published: newest first; scheduled: the next to go live first; trash: most recently trashed first; everything
        // else: most recently changed first.
        var ordered = query.Status switch
        {
            PostListStatus.Published => posts.OrderByDescending(p => p.PublishedOn).ThenByDescending(p => p.Id),
            PostListStatus.Scheduled => posts.OrderBy(p => p.PublishedOn).ThenBy(p => p.Id),
            PostListStatus.Trash => posts.OrderByDescending(p => p.DeletedOn).ThenByDescending(p => p.Id),
            _ => posts.OrderByDescending(p => p.ModifiedOn).ThenByDescending(p => p.Id)
        };

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.Status,
                p.PublishedOn,
                p.PublishedDateLocal,
                p.ModifiedOn,
                Tags = p.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                p.WordCount,
                p.IsFeatured,
                p.DeletedOn,
                p.RowVersion
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new PostSummaryDto
        {
            Id = r.Id,
            Title = r.Title,
            Slug = r.Slug,
            Status = r.Status,
            PublishedOn = r.PublishedOn,
            ModifiedOn = r.ModifiedOn,
            Tags = r.Tags,
            WordCount = r.WordCount,
            IsFeatured = r.IsFeatured,
            DeletedOn = r.DeletedOn,
            PublicPath = r.DeletedOn is null ? PublicPathOf(r.Status, r.PublishedDateLocal, r.Slug) : null,
            RowVersion = r.RowVersion
        }).ToList();

        return new PagedResult<PostSummaryDto>(items, totalCount, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<PostEditDto?> GetPostAsync(int id, CancellationToken cancellationToken = default)
    {
        var post = await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Tags)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return post is null ? null : await ToEditDtoAsync(post, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> CreateAsync(PostEditDto post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if ((Validate(post) ?? await ValidateImagesAsync(post, cancellationToken)) is { } invalid)
        {
            return invalid;
        }

        return await SaveWithRetryAsync(async ct =>
        {
            var settings = await settingsService.GetAsync(ct);
            var entity = new Post { Status = PostStatus.Draft };
            dbContext.Posts.Add(entity);

            await ApplyEditsAsync(entity, post, settings, ct);
            AddRevision(entity, RevisionKind.Manual);

            await dbContext.SaveChangesAsync(ct);
            return new PostSaved(await ToEditDtoAsync(entity, ct));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> UpdateAsync(int id, PostEditDto post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if ((Validate(post, requireRowVersion: true) ?? await ValidateImagesAsync(post, cancellationToken)) is { } invalid)
        {
            return invalid;
        }

        return await SaveWithRetryAsync(async ct =>
        {
            var entity = await LoadForEditAsync(id, ct);
            if (entity is null)
            {
                return PostSaveResult.NotFound;
            }

            if (!RowVersionMatches(entity, post.RowVersion))
            {
                return PostSaveResult.Conflict;
            }

            var settings = await settingsService.GetAsync(ct);
            var oldPath = LivePathOf(entity);
            var contentChanged = entity.Title != post.Title.Trim() || entity.ContentMarkdown != (post.ContentMarkdown ?? string.Empty);

            await ApplyEditsAsync(entity, post, settings, ct);
            if (contentChanged)
            {
                AddRevision(entity, RevisionKind.Manual);

                // Readers see "Updated …" only for changes to a post that was already live (P15); editing a draft or a
                // scheduled post is still part of writing it.
                if (oldPath is not null)
                {
                    entity.LastUpdatedOn = timeProvider.GetUtcNow();
                }
            }

            await AddRedirectIfMovedAsync(oldPath, entity, ct);
            MarkModified(entity);

            await dbContext.SaveChangesAsync(ct);
            if (entity.Status == PostStatus.Published)
            {
                await cacheInvalidator.PostChangedAsync(entity.Id);
            }

            return new PostSaved(await ToEditDtoAsync(entity, ct));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> AutosaveAsync(int id, PostEditDto post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if ((Validate(post, requireRowVersion: true) ?? await ValidateImagesAsync(post, cancellationToken)) is { } invalid)
        {
            return invalid;
        }

        return await SaveWithRetryAsync(async ct =>
        {
            var entity = await LoadForEditAsync(id, ct);
            if (entity is null)
            {
                return PostSaveResult.NotFound;
            }

            if (!RowVersionMatches(entity, post.RowVersion))
            {
                return PostSaveResult.Conflict;
            }

            // Published content only changes on an explicit Update (Q3), so a published post gets just
            // the revision, which ToEditDtoAsync then reports as pending changes.
            if (entity.Status == PostStatus.Draft)
            {
                var settings = await settingsService.GetAsync(ct);
                await ApplyEditsAsync(entity, post, settings, ct);
                MarkModified(entity);
            }

            dbContext.PostRevisions.Add(new PostRevision
            {
                Post = entity,
                Title = post.Title.Trim(),
                ContentMarkdown = post.ContentMarkdown ?? string.Empty,
                SavedOn = timeProvider.GetUtcNow(),
                Kind = RevisionKind.Autosave
            });

            await dbContext.SaveChangesAsync(ct);
            await PruneAutosavesAsync(entity.Id, ct);

            return new PostSaved(await ToEditDtoAsync(entity, ct));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> PublishAsync(int id, PublishPostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await SaveWithRetryAsync(async ct =>
        {
            var entity = await LoadForEditAsync(id, ct);
            if (entity is null)
            {
                return PostSaveResult.NotFound;
            }

            if (request.RowVersion is { Length: > 0 } && !RowVersionMatches(entity, request.RowVersion))
            {
                return PostSaveResult.Conflict;
            }

            var now = timeProvider.GetUtcNow();
            var settings = await settingsService.GetAsync(ct);
            var oldPath = LivePathOf(entity);

            // An explicit date wins, and a future one schedules the post (design 6.3, A9): it stays hidden until then,
            // and ScheduledPublishWatcher evicts the public caches when the time comes. Without one, a post that was
            // live before keeps its original date across unpublishing and republishing, and anything else (a new
            // post, or a scheduled one published early) goes live now.
            var keepsLiveDate = request.PublishOn is null && entity.PublishedOn is { } existing && existing <= now;
            var publishOn = (request.PublishOn ?? (keepsLiveDate ? entity.PublishedOn!.Value : now)).ToUniversalTime();

            // Republishing a post that was live before, under its original date, with content edited while it was a
            // draft, is an update of that post as far as readers are concerned (P15).
            if (keepsLiveDate && await ContentChangedSinceLastPublishAsync(entity, ct))
            {
                entity.LastUpdatedOn = now;
            }

            entity.Status = PostStatus.Published;
            entity.PublishedOn = publishOn;
            entity.PublishedDateLocal = BlogTimeZone.ToLocalDate(publishOn, settings.TimeZoneId);

            // "Close comments after N days" is applied at publish time (design 8.5, C7), counted from the publish date,
            // so a scheduled post gets its full N days once it is live. Changing the setting later leaves published
            // posts as they are; publishing again recomputes it.
            entity.CommentsCloseOn = CommentsCloseOn(publishOn, settings.CloseCommentsAfterDays);

            await AddPublishRevisionAsync(entity, ct);
            await AddRedirectIfMovedAsync(oldPath, entity, ct);
            MarkModified(entity);

            await dbContext.SaveChangesAsync(ct);
            await cacheInvalidator.PostChangedAsync(entity.Id);
            PostLog.PostPublished(logger, entity.Id, entity.Slug, publishOn, publishOn > now, userService.UserId);

            return new PostSaved(await ToEditDtoAsync(entity, ct));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> UnpublishAsync(int id, UnpublishPostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await SaveWithRetryAsync(async ct =>
        {
            var entity = await LoadForEditAsync(id, ct);
            if (entity is null)
            {
                return PostSaveResult.NotFound;
            }

            if (request.RowVersion is { Length: > 0 } && !RowVersionMatches(entity, request.RowVersion))
            {
                return PostSaveResult.Conflict;
            }

            if (entity.Status != PostStatus.Draft)
            {
                var wasScheduled = ReturnToDraft(entity);
                MarkModified(entity);
                await dbContext.SaveChangesAsync(ct);
                await cacheInvalidator.PostChangedAsync(entity.Id);
                PostLog.PostUnpublished(logger, entity.Id, entity.Slug, wasScheduled, userService.UserId);
            }

            return new PostSaved(await ToEditDtoAsync(entity, ct));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Posts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        // SoftDeleteInterceptor turns this into IsDeleted = true (the trash), which hides the post everywhere.
        var wasPublished = entity.Status == PostStatus.Published;
        dbContext.Posts.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasPublished)
        {
            await cacheInvalidator.PostChangedAsync(entity.Id);
        }

        PostLog.PostTrashed(logger, entity.Id, entity.Slug, wasPublished, userService.UserId);
        return true;
    }

    /// <inheritdoc />
    public async Task<PostSaveResult> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Include(p => p.Tags)
            .SingleOrDefaultAsync(p => p.Id == id && p.IsDeleted, cancellationToken);
        if (entity is null)
        {
            return PostSaveResult.NotFound;
        }

        // Always a draft, so restoring never puts anything back on the public site by surprise; the slug can't collide
        // because trashed posts keep theirs (the unique index covers the trash).
        entity.IsDeleted = false;
        entity.DeletedOn = null;
        ReturnToDraft(entity);

        await dbContext.SaveChangesAsync(cancellationToken);
        PostLog.PostRestored(logger, entity.Id, entity.Slug, userService.UserId);

        return new PostSaved(await ToEditDtoAsync(entity, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeletePermanentlyAsync(int id, CancellationToken cancellationToken = default)
    {
        return await PurgeAsync(id, cancellationToken) > 0;
    }

    /// <inheritdoc />
    public Task<int> EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        return PurgeAsync(onlyPostId: null, cancellationToken);
    }

    /// <summary>
    /// Hard-deletes posts in the trash (one, or all of them) and removes the redirects that pointed at their URLs.
    /// </summary>
    /// <remarks>
    /// The posts are removed through the change tracker, so <c>AuditInterceptor</c> records the deletes; because they are
    /// already in the trash, <c>SoftDeleteInterceptor</c> lets them through as real deletes. Their dependent rows are not
    /// loaded: the database's cascades remove them (design 6.8). The redirect clean-up runs afterwards and on its own: if
    /// it failed, the only effect would be stale redirects to a URL that answers 404, which is what it answers anyway.
    /// </remarks>
    private async Task<int> PurgeAsync(int? onlyPostId, CancellationToken cancellationToken)
    {
        var posts = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => p.IsDeleted && (onlyPostId == null || p.Id == onlyPostId))
            .ToListAsync(cancellationToken);
        if (posts.Count == 0)
        {
            return 0;
        }

        // Any URL the post had (including one it kept after being unpublished) may still be the target of old redirects.
        var paths = posts
            .Where(p => p.PublishedDateLocal is not null && p.Slug.Length > 0)
            .Select(p => PostPaths.Post(p.PublishedDateLocal!.Value, p.Slug))
            .ToList();

        dbContext.Posts.RemoveRange(posts);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (paths.Count > 0)
        {
            await dbContext.Redirects.Where(r => paths.Contains(r.ToPath)).ExecuteDeleteAsync(cancellationToken);
        }

        PostLog.PostsPurged(logger, posts.Count, string.Join(',', posts.Select(p => p.Id)), userService.UserId);
        return posts.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostRevisionSummaryDto>?> GetRevisionsAsync(int postId, CancellationToken cancellationToken = default)
    {
        // The revisions' query filter already hides a trashed post's revisions; this tells "no post" from "none yet".
        if (!await dbContext.Posts.AnyAsync(p => p.Id == postId, cancellationToken))
        {
            return null;
        }

        return await dbContext.PostRevisions
            .AsNoTracking()
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.SavedOn)
            .ThenByDescending(r => r.Id)
            .Select(r => new PostRevisionSummaryDto
            {
                Id = r.Id,
                Kind = r.Kind,
                SavedOn = r.SavedOn,
                Title = r.Title,
                ContentLength = r.ContentMarkdown.Length
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PostRevisionDto?> GetRevisionAsync(int postId, int revisionId, CancellationToken cancellationToken = default)
    {
        return await dbContext.PostRevisions
            .AsNoTracking()
            .Where(r => r.Id == revisionId && r.PostId == postId)
            .Select(r => new PostRevisionDto
            {
                Id = r.Id,
                PostId = r.PostId,
                Kind = r.Kind,
                SavedOn = r.SavedOn,
                Title = r.Title,
                ContentMarkdown = r.ContentMarkdown
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SlugCheckResult> CheckSlugAsync(SlugCheckRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? SlugFromTitle(request.Title)
            : request.Slug.Trim();

        var probe = new PostEditDto { Title = "-", Slug = slug };
        if (postValidator.Validate(probe).ToDictionary().TryGetValue(nameof(PostEditDto.Slug), out var errors))
        {
            var suggestion = SlugGenerator.Generate(slug, SlugGenerator.DefaultMaxLength);
            return new SlugCheckResult(slug, IsValid: false, IsAvailable: false,
                suggestion.Length > 0 ? suggestion : null, errors[0]);
        }

        var taken = await LoadTakenSlugsAsync(slug, request.PostId ?? 0, cancellationToken);
        var unique = SlugGenerator.MakeUnique(slug, taken.Contains);
        var isAvailable = unique == slug;

        return new SlugCheckResult(slug, IsValid: true, isAvailable, unique,
            isAvailable ? null : $"Another post already uses '{slug}'. Saving will use '{unique}'.");
    }

    /// <summary>
    /// Runs <paramref name="operation"/>, mapping a concurrency failure to <see cref="PostConflict"/> and
    /// retrying after a lost unique-index race (tag or slug), which the retry resolves by seeing the winner's row.
    /// </summary>
    private async Task<PostSaveResult> SaveWithRetryAsync(Func<CancellationToken, Task<PostSaveResult>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                return PostSaveResult.Conflict;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation() && attempt < MaxSaveAttempts)
            {
                logger.LogInformation(ex, "Post save lost a unique-index race on attempt {Attempt}; retrying.", attempt);

                // Drop the failed attempt's entities so the retry reloads everything, including the winner's rows.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// The save pipeline (design 6.2): copies the editable fields onto <paramref name="post"/> and recomputes
    /// everything derived from them.
    /// </summary>
    private async Task ApplyEditsAsync(Post post, PostEditDto dto, SiteSettingsDto settings, CancellationToken cancellationToken)
    {
        var title = dto.Title.Trim();
        var markdown = dto.ContentMarkdown ?? string.Empty;

        // Both need the values from before this save to tell "still generated" from "typed by the author".
        await ResolveSlugAsync(post, title, dto.Slug?.Trim(), cancellationToken);
        post.Summary = ResolveSummary(post, dto.Summary?.Trim(), markdown);

        post.Title = title;
        post.ContentMarkdown = markdown;
        var rendered = await contentRenderer.RenderAsync(markdown, cancellationToken);
        post.ContentHtml = rendered.Html;
        post.HasCodeBlocks = rendered.ContainsCodeBlocks;

        var stats = ReadingTime.Calculate(markdown);
        post.WordCount = stats.WordCount;
        post.ReadingMinutes = stats.ReadingMinutes;

        post.AllowComments = dto.AllowComments;
        post.IsFeatured = dto.IsFeatured;
        post.MetaTitle = NullIfBlank(dto.MetaTitle);
        post.MetaDescription = NullIfBlank(dto.MetaDescription);
        post.CoverMediaId = dto.CoverMediaId;
        post.SocialImageMediaId = dto.SocialImageMediaId;

        // Recomputed on every save so it follows the configured time zone (Q10).
        if (post.PublishedOn is { } publishedOn)
        {
            post.PublishedDateLocal = BlogTimeZone.ToLocalDate(publishedOn, settings.TimeZoneId);
        }

        await SyncTagsAsync(post, dto.Tags ?? [], cancellationToken);
        SyncMedia(post, rendered.MediaItemIds);
    }

    /// <summary>
    /// Sets the slug (design 7.2): generated from the title until the first publish, unless the author typed
    /// their own; locked afterwards except for an explicit change. The result is made unique with a
    /// <c>-2</c>, <c>-3</c>, … suffix.
    /// </summary>
    /// <remarks>
    /// The editor sends back the slug it was given, so "the author didn't customize it" is detected by the
    /// stored slug still matching the stored title's generated slug.
    /// </remarks>
    private async Task ResolveSlugAsync(Post post, string newTitle, string? requestedSlug, CancellationToken cancellationToken)
    {
        var neverPublished = post.PublishedOn is null;
        var followsTitle = neverPublished && IsGeneratedSlug(post.Slug, post.Title);

        var desired = requestedSlug switch
        {
            null or "" when neverPublished || post.Slug.Length == 0 => SlugFromTitle(newTitle),
            null or "" => post.Slug,
            _ when requestedSlug == post.Slug && followsTitle => SlugFromTitle(newTitle),
            _ => requestedSlug
        };

        if (desired == post.Slug)
        {
            return;
        }

        var taken = await LoadTakenSlugsAsync(desired, post.Id, cancellationToken);
        post.Slug = SlugGenerator.MakeUnique(desired, taken.Contains);
    }

    /// <summary>Whether <paramref name="slug"/> is the title's generated slug, possibly with a collision suffix.</summary>
    private static bool IsGeneratedSlug(string slug, string title)
    {
        if (slug.Length == 0)
        {
            return true;
        }

        var generated = SlugFromTitle(title);
        return slug == generated
            || (slug.StartsWith(generated + "-", StringComparison.Ordinal)
                && int.TryParse(slug.AsSpan(generated.Length + 1), out var number)
                && number >= 2);
    }

    /// <summary>
    /// The author's summary, or one generated from the content when it is blank or is still the summary
    /// generated from the previous content.
    /// </summary>
    private static string ResolveSummary(Post post, string? requestedSummary, string markdown)
    {
        var wasGenerated = post.Summary == SummaryGenerator.FromMarkdown(post.ContentMarkdown);
        var regenerate = string.IsNullOrEmpty(requestedSummary) || (wasGenerated && requestedSummary == post.Summary);

        return regenerate ? SummaryGenerator.FromMarkdown(markdown) : requestedSummary!;
    }

    /// <summary>Replaces the post's tags with <paramref name="names"/>, creating unknown tags (T1.3).</summary>
    private async Task SyncTagsAsync(Post post, IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var tags = await PostTagResolver.ResolveAsync(dbContext, names, cancellationToken);
        var wanted = tags.Select(t => t.NormalizedName).ToHashSet(StringComparer.Ordinal);

        post.Tags.RemoveAll(t => !wanted.Contains(t.NormalizedName));
        foreach (var tag in tags.Where(t => !post.Tags.Any(existing => existing.NormalizedName == t.NormalizedName)))
        {
            post.Tags.Add(tag);
        }
    }

    /// <summary>
    /// Rebuilds the post's <see cref="PostMedia"/> rows (T2.12) from the library items its Markdown references, as
    /// found by the renderer, so "Used in N posts" and the unused filter follow every save.
    /// </summary>
    private static void SyncMedia(Post post, IReadOnlyList<int> mediaIds)
    {
        post.PostMedia.RemoveAll(pm => !mediaIds.Contains(pm.MediaItemId));
        foreach (var mediaId in mediaIds.Where(id => !post.PostMedia.Any(pm => pm.MediaItemId == id)))
        {
            post.PostMedia.Add(new PostMedia { MediaItemId = mediaId });
        }
    }

    /// <summary>
    /// Slugs equal to <paramref name="slug"/> or starting with <c>slug-</c>, used by any post other than
    /// <paramref name="excludePostId"/>. Trashed posts count: the unique index covers them too.
    /// </summary>
    private async Task<HashSet<string>> LoadTakenSlugsAsync(string slug, int excludePostId, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => p.Id != excludePostId && (p.Slug == slug || p.Slug.StartsWith(prefix)))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);

        return taken.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Records a redirect when a post that was live before this save is published at a new URL (T1.5). The old URL
    /// must have been live: a scheduled post that is re-dated was never reachable, so its old URL needs no redirect.
    /// </summary>
    private async Task AddRedirectIfMovedAsync(string? oldPath, Post post, CancellationToken cancellationToken)
    {
        if (oldPath is not null && PublicPathOf(post) is { } newPath && newPath != oldPath)
        {
            await RedirectWriter.AddAsync(dbContext, oldPath, newPath, cancellationToken);
        }
    }

    /// <summary>Deletes all but the newest <see cref="AutosavesToKeep"/> autosaves of a post.</summary>
    private async Task PruneAutosavesAsync(int postId, CancellationToken cancellationToken)
    {
        var staleIds = await dbContext.PostRevisions
            .Where(r => r.PostId == postId && r.Kind == RevisionKind.Autosave)
            .OrderByDescending(r => r.SavedOn)
            .ThenByDescending(r => r.Id)
            .Skip(AutosavesToKeep)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count > 0)
        {
            await dbContext.PostRevisions.Where(r => staleIds.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>Loads a tracked post with the collections the save pipeline rewrites.</summary>
    private Task<Post?> LoadForEditAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.Posts
            .Include(p => p.Tags)
            .Include(p => p.PostMedia)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <summary>
    /// Records the published content as a <see cref="RevisionKind.Publish"/> revision, unless the newest revision
    /// already is exactly that publish: rescheduling or re-dating a post republishes the same content, and a history
    /// full of identical entries would bury the real changes.
    /// </summary>
    private async Task AddPublishRevisionAsync(Post post, CancellationToken cancellationToken)
    {
        var latest = await dbContext.PostRevisions
            .AsNoTracking()
            .Where(r => r.PostId == post.Id)
            .OrderByDescending(r => r.SavedOn)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Kind, r.Title, r.ContentMarkdown })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is { Kind: RevisionKind.Publish } && latest.Title == post.Title && latest.ContentMarkdown == post.ContentMarkdown)
        {
            return;
        }

        AddRevision(post, RevisionKind.Publish);
    }

    /// <summary>
    /// Whether the post's title or content differs from its most recent <see cref="RevisionKind.Publish"/> revision, that
    /// is, from what readers last saw. A post with no publish revision counts as unchanged.
    /// </summary>
    private async Task<bool> ContentChangedSinceLastPublishAsync(Post post, CancellationToken cancellationToken)
    {
        var published = await dbContext.PostRevisions
            .AsNoTracking()
            .Where(r => r.PostId == post.Id && r.Kind == RevisionKind.Publish)
            .OrderByDescending(r => r.SavedOn)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Title, r.ContentMarkdown })
            .FirstOrDefaultAsync(cancellationToken);

        return published is not null && (published.Title != post.Title || published.ContentMarkdown != post.ContentMarkdown);
    }

    /// <summary>
    /// Turns a published or scheduled post back into a draft. PublishedOn and PublishedDateLocal are kept, so republishing
    /// restores the same URL. A scheduled post forgets its date instead: it was never live at it, and the slug should
    /// follow the title again until the post really is published.
    /// </summary>
    /// <returns>Whether the post was scheduled.</returns>
    private bool ReturnToDraft(Post post)
    {
        var wasScheduled = PostSchedule.IsScheduled(post.Status, post.PublishedOn, timeProvider.GetUtcNow());
        if (wasScheduled)
        {
            post.PublishedOn = null;
            post.PublishedDateLocal = null;
        }

        post.Status = PostStatus.Draft;
        return wasScheduled;
    }

    /// <summary>Adds a revision of the post's current title and content.</summary>
    private void AddRevision(Post post, RevisionKind kind)
    {
        dbContext.PostRevisions.Add(new PostRevision
        {
            Post = post,
            Title = post.Title,
            ContentMarkdown = post.ContentMarkdown,
            SavedOn = timeProvider.GetUtcNow(),
            Kind = kind
        });
    }

    /// <summary>
    /// Forces an UPDATE of the post row even when only its tags or media changed, so its
    /// <see cref="Post.RowVersion"/> and modified stamp move and other tabs see the save as a conflict.
    /// </summary>
    private void MarkModified(Post post)
    {
        dbContext.Entry(post).Property(p => p.Title).IsModified = true;
    }

    /// <summary>Builds the editor DTO, including pending autosaved changes for a published post.</summary>
    private async Task<PostEditDto> ToEditDtoAsync(Post post, CancellationToken cancellationToken)
    {
        var (cover, social) = await LoadImagesAsync(post, cancellationToken);

        return new PostEditDto
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Summary = post.Summary,
            ContentMarkdown = post.ContentMarkdown,
            Tags = [.. post.Tags.Select(t => t.Name).Order(StringComparer.OrdinalIgnoreCase)],
            AllowComments = post.AllowComments,
            CommentsCloseOn = post.CommentsCloseOn,
            IsFeatured = post.IsFeatured,
            MetaTitle = post.MetaTitle,
            MetaDescription = post.MetaDescription,
            CoverMediaId = post.CoverMediaId,
            CoverImage = cover,
            SocialImageMediaId = post.SocialImageMediaId,
            SocialImage = social,
            RowVersion = post.RowVersion,
            Status = post.Status,
            PublishedOn = post.PublishedOn,
            PublishedDateLocal = post.PublishedDateLocal,
            LastUpdatedOn = post.LastUpdatedOn,
            ModifiedOn = post.ModifiedOn,
            WordCount = post.WordCount,
            ReadingMinutes = post.ReadingMinutes,
            PublicPath = PublicPathOf(post),
            PendingChanges = post.Status == PostStatus.Published
                ? await LoadPendingChangesAsync(post, cancellationToken)
                : null
        };
    }

    /// <summary>
    /// The newest autosave of a published post if it was recorded after the post's last save and differs
    /// from the live content; otherwise <see langword="null"/>.
    /// </summary>
    private async Task<PostPendingChangesDto?> LoadPendingChangesAsync(Post post, CancellationToken cancellationToken)
    {
        var latest = await dbContext.PostRevisions
            .AsNoTracking()
            .Where(r => r.PostId == post.Id && r.Kind == RevisionKind.Autosave)
            .OrderByDescending(r => r.SavedOn)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var isPending = latest is not null
            && latest.SavedOn > post.ModifiedOn
            && (latest.Title != post.Title || latest.ContentMarkdown != post.ContentMarkdown);

        return isPending
            ? new PostPendingChangesDto { Title = latest!.Title, ContentMarkdown = latest.ContentMarkdown, SavedOn = latest.SavedOn }
            : null;
    }

    /// <summary>Validates the DTO with <see cref="PostEditValidator"/>, returning <see langword="null"/> when it is valid.</summary>
    private PostInvalid? Validate(PostEditDto post, bool requireRowVersion = false)
    {
        var errors = postValidator.Validate(post).ToDictionary();
        if (requireRowVersion && post.RowVersion is not { Length: > 0 })
        {
            errors[nameof(PostEditDto.RowVersion)] = ["The post's version is missing. Reload the post and try again."];
        }

        return errors.Count == 0 ? null : new PostInvalid(errors.AsReadOnly());
    }

    /// <summary>
    /// Checks that the cover and social images (A15, A16) are library items that still exist, returning
    /// <see langword="null"/> when they do. An item deleted while the editor was open is reported on its field.
    /// </summary>
    private async Task<PostInvalid?> ValidateImagesAsync(PostEditDto post, CancellationToken cancellationToken)
    {
        int[] ids = [.. new[] { post.CoverMediaId, post.SocialImageMediaId }.OfType<int>().Distinct()];
        if (ids.Length == 0)
        {
            return null;
        }

        var existing = await dbContext.MediaItems.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken);
        var errors = new Dictionary<string, string[]>();
        if (post.CoverMediaId is { } coverId && !existing.Contains(coverId))
        {
            errors[nameof(PostEditDto.CoverMediaId)] = ["The cover image is no longer in the media library. Choose another one."];
        }

        if (post.SocialImageMediaId is { } socialId && !existing.Contains(socialId))
        {
            errors[nameof(PostEditDto.SocialImageMediaId)] = ["The social image is no longer in the media library. Choose another one."];
        }

        return errors.Count == 0 ? null : new PostInvalid(errors.AsReadOnly());
    }

    /// <summary>Loads the thumbnails of the post's cover and social images.</summary>
    private async Task<(PostImageDto? Cover, PostImageDto? Social)> LoadImagesAsync(Post post, CancellationToken cancellationToken)
    {
        int[] ids = [.. new[] { post.CoverMediaId, post.SocialImageMediaId }.OfType<int>().Distinct()];
        if (ids.Length == 0)
        {
            return (null, null);
        }

        var images = await dbContext.MediaItems
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.PublicId, m.FileName, m.Version, m.AltText, m.Width, m.Height })
            .ToListAsync(cancellationToken);
        var byId = images.ToDictionary(
            m => m.Id,
            m => new PostImageDto(m.Id, MediaPaths.Versioned(m.PublicId, m.FileName, m.Version), m.AltText, m.Width, m.Height));

        return (post.CoverMediaId is { } coverId ? byId.GetValueOrDefault(coverId) : null,
            post.SocialImageMediaId is { } socialId ? byId.GetValueOrDefault(socialId) : null);
    }

    /// <summary>Whether the caller's concurrency token matches the stored one.</summary>
    private static bool RowVersionMatches(Post post, byte[]? rowVersion)
    {
        return rowVersion is not null && post.RowVersion.AsSpan().SequenceEqual(rowVersion);
    }

    /// <summary>The post's public URL path while it is published (including scheduled, where it is the URL to come).</summary>
    private static string? PublicPathOf(Post post)
    {
        return PublicPathOf(post.Status, post.PublishedDateLocal, post.Slug);
    }

    /// <summary>The post's public URL path if readers can reach it right now, otherwise <see langword="null"/>.</summary>
    private string? LivePathOf(Post post)
    {
        return PostSchedule.IsLive(post.Status, post.PublishedOn, timeProvider.GetUtcNow()) ? PublicPathOf(post) : null;
    }

    /// <summary>The public URL path of a published post with this date and slug, otherwise <see langword="null"/>.</summary>
    private static string? PublicPathOf(PostStatus status, DateOnly? publishedDateLocal, string slug)
    {
        return status == PostStatus.Published && publishedDateLocal is { } date && slug.Length > 0
            ? PostPaths.Post(date, slug)
            : null;
    }

    /// <summary>The slug generated from a title, with a fallback for titles that yield nothing.</summary>
    private static string SlugFromTitle(string? title)
    {
        var slug = SlugGenerator.Generate(title);
        return slug.Length > 0 ? slug : FallbackSlug;
    }

    /// <summary>When comments on a post published at <paramref name="publishOn"/> close; <see langword="null"/> for never (0 days).</summary>
    public static DateTimeOffset? CommentsCloseOn(DateTimeOffset publishOn, int closeAfterDays)
    {
        return closeAfterDays > 0 ? publishOn.AddDays(closeAfterDays) : null;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
