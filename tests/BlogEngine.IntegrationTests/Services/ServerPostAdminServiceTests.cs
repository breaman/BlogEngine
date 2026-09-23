using BlogEngine.Data.Models;
using BlogEngine.Data.Queries;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests the server <see cref="IPostAdminService"/> against the migrated database (T1.2–T1.6): the save
/// pipeline, slugs, concurrency, tags, publishing, redirects and autosave.
/// </summary>
/// <remarks>
/// Each call runs in its own DI scope, as a request would. Posts are never cleaned up, so every test uses
/// titles and tag names made unique with <see cref="Unique"/>.
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class ServerPostAdminServiceTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Creating a post runs the whole save pipeline and stores a draft.</summary>
    [Test]
    public async Task CreateAsync_RunsSavePipeline()
    {
        var title = $"Pipeline {Unique()}";
        var result = await CreateAsync(new PostEditDto
        {
            Title = $"  {title}  ",
            ContentMarkdown = "First paragraph with **bold** words.\n\n<script>alert(1)</script>\n\n```cs\nvar ignored = true;\n```",
            Tags = ["Alpha" + Unique()]
        });

        await Assert.That(result.Title).IsEqualTo(title);
        await Assert.That(result.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(result.Slug).IsEqualTo(title.ToLowerInvariant().Replace(' ', '-'));
        await Assert.That(result.Summary).IsEqualTo("First paragraph with bold words.");
        await Assert.That(result.WordCount).IsEqualTo(5);
        await Assert.That(result.ReadingMinutes).IsEqualTo(1);
        await Assert.That(result.RowVersion).IsNotNull().And.IsNotEmpty();
        await Assert.That(result.PublishedOn).IsNull();
        await Assert.That(result.PublicPath).IsNull();

        var stored = await FindAsync(result.Id);
        await Assert.That(stored.ContentHtml).Contains("<strong>bold</strong>");
        await Assert.That(stored.ContentHtml).DoesNotContain("<script");
    }

    /// <summary>Invalid input is rejected with field errors and nothing is saved.</summary>
    [Test]
    public async Task CreateAsync_Invalid_ReturnsErrors()
    {
        var result = await WithServiceAsync(s => s.CreateAsync(new PostEditDto { Title = " ", Slug = "Not A Slug" }));

        await Assert.That(result).IsTypeOf<PostInvalid>();
        await Assert.That(((PostInvalid)result).Errors.Keys).IsEquivalentTo([nameof(PostEditDto.Title), nameof(PostEditDto.Slug)]);
    }

    /// <summary>A slug already in use gets a <c>-2</c>, then <c>-3</c>, suffix.</summary>
    [Test]
    public async Task CreateAsync_DuplicateTitle_SuffixesSlug()
    {
        var title = $"Duplicate {Unique()}";

        var first = await CreateAsync(new PostEditDto { Title = title });
        var second = await CreateAsync(new PostEditDto { Title = title });
        var third = await CreateAsync(new PostEditDto { Title = title, Slug = first.Slug });

        await Assert.That(second.Slug).IsEqualTo(first.Slug + "-2");
        await Assert.That(third.Slug).IsEqualTo(first.Slug + "-3");
    }

    /// <summary>A trashed post still owns its slug, so restoring it can never collide.</summary>
    [Test]
    public async Task CreateAsync_SlugOfTrashedPost_IsStillTaken()
    {
        var title = $"Trashed {Unique()}";
        var trashed = await CreateAsync(new PostEditDto { Title = title });
        await WithServiceAsync(s => s.DeleteAsync(trashed.Id));

        var replacement = await CreateAsync(new PostEditDto { Title = title });

        await Assert.That(replacement.Slug).IsEqualTo(trashed.Slug + "-2");
    }

    /// <summary>Updating saves the edits and returns a new concurrency token.</summary>
    [Test]
    public async Task UpdateAsync_SavesChanges()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Update {Unique()}", ContentMarkdown = "Old." });
        created.ContentMarkdown = "New content here.";
        created.IsFeatured = true;
        created.MetaTitle = "  SEO title  ";

        var updated = await UpdateAsync(created);

        await Assert.That(updated.ContentMarkdown).IsEqualTo("New content here.");
        await Assert.That(updated.Summary).IsEqualTo("New content here.");
        await Assert.That(updated.IsFeatured).IsTrue();
        await Assert.That(updated.MetaTitle).IsEqualTo("SEO title");
        await Assert.That(updated.RowVersion).IsNotEquivalentTo(created.RowVersion!);
    }

    /// <summary>
    /// Saving with a stale <c>RowVersion</c> (the post changed in another tab) is a conflict, and the other
    /// tab's changes survive.
    /// </summary>
    [Test]
    public async Task UpdateAsync_StaleRowVersion_ReturnsConflict()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Tabs {Unique()}", ContentMarkdown = "Original." });
        var tabA = Copy(created);
        var tabB = Copy(created);

        tabA.ContentMarkdown = "Saved in tab A.";
        await UpdateAsync(tabA);

        tabB.ContentMarkdown = "Saved in tab B.";
        var result = await WithServiceAsync(s => s.UpdateAsync(tabB.Id, tabB));

        await Assert.That(result).IsTypeOf<PostConflict>();
        await Assert.That((await FindAsync(created.Id)).ContentMarkdown).IsEqualTo("Saved in tab A.");
    }

    /// <summary>A change to tags alone still moves the row version, so other tabs see a conflict.</summary>
    [Test]
    public async Task UpdateAsync_TagOnlyChange_BumpsRowVersion()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Tag only {Unique()}" });
        created.Tags = ["Solo" + Unique()];

        var updated = await UpdateAsync(created);

        await Assert.That(updated.RowVersion).IsNotEquivalentTo(created.RowVersion!);
    }

    /// <summary>An update without a <c>RowVersion</c> is rejected rather than silently overwriting.</summary>
    [Test]
    public async Task UpdateAsync_MissingRowVersion_IsInvalid()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"No version {Unique()}" });
        created.RowVersion = null;

        var result = await WithServiceAsync(s => s.UpdateAsync(created.Id, created));

        await Assert.That(((PostInvalid)result).Errors.Keys).Contains(nameof(PostEditDto.RowVersion));
    }

    /// <summary>Updating a post that doesn't exist is reported as not found.</summary>
    [Test]
    public async Task UpdateAsync_UnknownPost_ReturnsNotFound()
    {
        var result = await WithServiceAsync(s => s.UpdateAsync(int.MaxValue, new PostEditDto { Title = "x", RowVersion = [1] }));

        await Assert.That(result).IsTypeOf<PostNotFound>();
    }

    /// <summary>
    /// While a draft, the slug follows the title unless the author typed their own; after publishing it's
    /// locked (design 7.2).
    /// </summary>
    [Test]
    public async Task Slug_FollowsTitleUntilPublished()
    {
        var token = Unique();
        var created = await CreateAsync(new PostEditDto { Title = $"First title {token}" });

        created.Title = $"Second title {token}";
        var retitled = await UpdateAsync(created);
        await Assert.That(retitled.Slug).IsEqualTo($"second-title-{token}");

        retitled.Slug = $"custom-{token}";
        var customized = await UpdateAsync(retitled);
        customized.Title = $"Third title {token}";
        var keptCustom = await UpdateAsync(customized);
        await Assert.That(keptCustom.Slug).IsEqualTo($"custom-{token}");

        keptCustom.Slug = null;
        var regenerated = await UpdateAsync(keptCustom);
        await Assert.That(regenerated.Slug).IsEqualTo($"third-title-{token}");

        var published = await PublishAsync(regenerated.Id);
        published.Title = $"Fourth title {token}";
        published.Slug = null;
        var locked = await UpdateAsync(published);
        await Assert.That(locked.Slug).IsEqualTo($"third-title-{token}");
    }

    /// <summary>A generated summary follows the content; one the author wrote is kept.</summary>
    [Test]
    public async Task Summary_FollowsContentUntilWritten()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Summary {Unique()}", ContentMarkdown = "First version." });

        created.ContentMarkdown = "Second version.";
        var regenerated = await UpdateAsync(created);
        await Assert.That(regenerated.Summary).IsEqualTo("Second version.");

        regenerated.Summary = "Written by hand.";
        var written = await UpdateAsync(regenerated);
        written.ContentMarkdown = "Third version.";
        var kept = await UpdateAsync(written);
        await Assert.That(kept.Summary).IsEqualTo("Written by hand.");
    }

    /// <summary>Library media referenced by the content is tracked in <c>PostMedia</c> and follows edits.</summary>
    [Test]
    public async Task Save_RebuildsPostMedia()
    {
        var (first, second) = (await AddMediaAsync(), await AddMediaAsync());
        var created = await CreateAsync(new PostEditDto
        {
            Title = $"Media {Unique()}",
            ContentMarkdown = $"![One](/media/{first.PublicId}/one.jpg)\n\n![Missing](/media/zzzzzzzzzzzz/none.jpg)"
        });
        await Assert.That(await MediaIdsOfAsync(created.Id)).IsEquivalentTo([first.Id]);

        created.ContentMarkdown = $"<img src=\"/media/{second.PublicId}/two.jpg\" alt=\"Two\">";
        await UpdateAsync(created);

        await Assert.That(await MediaIdsOfAsync(created.Id)).IsEquivalentTo([second.Id]);
    }

    /// <summary>Typing an existing tag in any casing reuses it with its original casing (T1.3).</summary>
    [Test]
    public async Task Tags_MatchExistingCaseInsensitively()
    {
        var name = $"TypeScript{Unique()}";
        var first = await CreateAsync(new PostEditDto { Title = $"Tags one {Unique()}", Tags = [name] });

        var second = await CreateAsync(new PostEditDto
        {
            Title = $"Tags two {Unique()}",
            Tags = [name.ToLowerInvariant(), $"  {name.ToUpperInvariant()} "]
        });

        await Assert.That(first.Tags).IsEquivalentTo([name]);
        await Assert.That(second.Tags).IsEquivalentTo([name]);
        await Assert.That(await CountTagsAsync(name)).IsEqualTo(1);
    }

    /// <summary>Removing a tag from a post leaves the tag in place for other posts.</summary>
    [Test]
    public async Task Tags_Removed_OnlyUnlinks()
    {
        var keep = $"Keep{Unique()}";
        var drop = $"Drop{Unique()}";
        var created = await CreateAsync(new PostEditDto { Title = $"Tag removal {Unique()}", Tags = [keep, drop] });

        created.Tags = [keep];
        var updated = await UpdateAsync(created);

        await Assert.That(updated.Tags).IsEquivalentTo([keep]);
        await Assert.That(await CountTagsAsync(drop)).IsEqualTo(1);
    }

    /// <summary>
    /// Concurrent saves creating the same new tag in different casings (<c>c#…</c> and <c>C#…</c>) end with
    /// exactly one tag, used by every post: the saves that lose the race on the unique index retry and reuse
    /// the winner's tag (design 6.4, T1.3).
    /// </summary>
    [Test]
    public async Task Tags_ConcurrentCreates_EndWithOneTag()
    {
        var token = Unique();
        string[] variants = [$"c#-{token}", $"C#-{token.ToUpperInvariant()}", $" c#-{token} "];

        // Several saves at once make the race on the unique index all but certain.
        var saves = Enumerable.Range(0, 8)
            .Select(i => CreateAsync(new PostEditDto { Title = $"Race {token} {i}", Tags = [variants[i % variants.Length]] }));
        var posts = await Task.WhenAll(saves);

        await Assert.That(await CountTagsAsync($"C#-{token}")).IsEqualTo(1);
        var tagNames = posts.Select(p => p.Tags.Single()).Distinct().ToList();
        await Assert.That(tagNames).Count().IsEqualTo(1);
    }

    /// <summary>A draft is never publicly visible; publishing makes it visible (design 6.3, T1.4).</summary>
    [Test]
    public async Task Publish_MakesDraftVisible()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Visibility {Unique()}", ContentMarkdown = "Hello." });
        await Assert.That(await IsPubliclyVisibleAsync(created.Id)).IsFalse();

        var published = await PublishAsync(created.Id);

        await Assert.That(published.Status).IsEqualTo(PostStatus.Published);
        await Assert.That(published.PublishedOn).IsNotNull();
        await Assert.That(published.PublishedDateLocal).IsNotNull();
        await Assert.That(published.PublicPath).IsEqualTo(PostPaths.Post(published.PublishedDateLocal!.Value, published.Slug!));
        await Assert.That(await IsPubliclyVisibleAsync(created.Id)).IsTrue();
        await Assert.That(await RevisionKindsAsync(created.Id)).Contains(RevisionKind.Publish);
    }

    /// <summary>Unpublishing hides the post again; republishing keeps the original publish date.</summary>
    [Test]
    public async Task Unpublish_HidesPost_AndRepublishKeepsDate()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Unpublish {Unique()}" });
        var published = await PublishAsync(created.Id);

        var unpublished = await ExpectSavedAsync(s => s.UnpublishAsync(created.Id, new UnpublishPostRequest { RowVersion = published.RowVersion }));
        await Assert.That(unpublished.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(unpublished.PublicPath).IsNull();
        await Assert.That(await IsPubliclyVisibleAsync(created.Id)).IsFalse();

        var republished = await PublishAsync(created.Id);
        await Assert.That(republished.PublishedOn).IsEqualTo(published.PublishedOn);
    }

    /// <summary>An explicit past date backdates the post.</summary>
    [Test]
    public async Task Publish_WithPastDate_Backdates()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Backdated {Unique()}" });
        var publishOn = new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero);

        var published = await ExpectSavedAsync(s => s.PublishAsync(created.Id, new PublishPostRequest { PublishOn = publishOn }));

        await Assert.That(published.PublishedOn).IsEqualTo(publishOn);
        await Assert.That(await IsPubliclyVisibleAsync(created.Id)).IsTrue();
    }

    /// <summary>Scheduling isn't supported yet (T4.1), so a future date is rejected and the post stays a draft.</summary>
    [Test]
    public async Task Publish_FutureDate_IsRejected()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Future {Unique()}" });

        var result = await WithServiceAsync(s => s.PublishAsync(created.Id,
            new PublishPostRequest { PublishOn = DateTimeOffset.UtcNow.AddDays(1) }));

        await Assert.That(((PostInvalid)result).Errors.Keys).IsEquivalentTo([nameof(PublishPostRequest.PublishOn)]);
        await Assert.That((await FindAsync(created.Id)).Status).IsEqualTo(PostStatus.Draft);
    }

    /// <summary>Publishing with a stale row version is a conflict.</summary>
    [Test]
    public async Task Publish_StaleRowVersion_ReturnsConflict()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Stale publish {Unique()}" });
        var stale = created.RowVersion;
        await UpdateAsync(created);

        var result = await WithServiceAsync(s => s.PublishAsync(created.Id, new PublishPostRequest { RowVersion = stale }));

        await Assert.That(result).IsTypeOf<PostConflict>();
    }

    /// <summary>
    /// Changing a published post's slug records a 301 from the old URL; a second change repoints the first
    /// redirect so every old URL reaches the newest in one hop (T1.5).
    /// </summary>
    [Test]
    public async Task SlugChange_OnPublishedPost_CreatesCollapsedRedirects()
    {
        var token = Unique();
        var published = await PublishAsync((await CreateAsync(new PostEditDto { Title = $"Moving {token}" })).Id);
        var firstPath = published.PublicPath!;

        published.Slug = $"moved-once-{token}";
        var movedOnce = await UpdateAsync(published);
        movedOnce.Slug = $"moved-twice-{token}";
        var movedTwice = await UpdateAsync(movedOnce);

        await Assert.That(await RedirectTargetAsync(firstPath)).IsEqualTo(movedTwice.PublicPath);
        await Assert.That(await RedirectTargetAsync(movedOnce.PublicPath!)).IsEqualTo(movedTwice.PublicPath);
    }

    /// <summary>Changing the slug back to an earlier one removes that URL's redirect instead of looping.</summary>
    [Test]
    public async Task SlugChange_BackToEarlierSlug_RemovesLoop()
    {
        var token = Unique();
        var published = await PublishAsync((await CreateAsync(new PostEditDto { Title = $"Round trip {token}" })).Id);
        var originalSlug = published.Slug;
        var originalPath = published.PublicPath!;

        published.Slug = $"detour-{token}";
        var detoured = await UpdateAsync(published);
        detoured.Slug = originalSlug;
        var back = await UpdateAsync(detoured);

        await Assert.That(back.PublicPath).IsEqualTo(originalPath);
        await Assert.That(await RedirectTargetAsync(originalPath)).IsNull();
        await Assert.That(await RedirectTargetAsync(detoured.PublicPath!)).IsEqualTo(originalPath);
    }

    /// <summary>Re-dating a published post redirects its old dated URL.</summary>
    [Test]
    public async Task DateChange_OnPublishedPost_CreatesRedirect()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Redated {Unique()}" });
        var published = await ExpectSavedAsync(s => s.PublishAsync(created.Id,
            new PublishPostRequest { PublishOn = new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero) }));

        var redated = await ExpectSavedAsync(s => s.PublishAsync(created.Id,
            new PublishPostRequest { PublishOn = new DateTimeOffset(2025, 2, 20, 12, 0, 0, TimeSpan.Zero) }));

        await Assert.That(await RedirectTargetAsync(published.PublicPath!)).IsEqualTo(redated.PublicPath);
    }

    /// <summary>Editing a draft's slug creates no redirect: its URL was never public.</summary>
    [Test]
    public async Task SlugChange_OnDraft_CreatesNoRedirect()
    {
        var token = Unique();
        var created = await CreateAsync(new PostEditDto { Title = $"Draft move {token}" });
        created.Slug = $"draft-moved-{token}";

        await UpdateAsync(created);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await dbContext.Redirects.AnyAsync(r => r.ToPath.Contains(token))).IsFalse();
    }

    /// <summary>Autosaving a draft updates the post and records an autosave revision (T1.6).</summary>
    [Test]
    public async Task Autosave_Draft_UpdatesPost()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Autosave draft {Unique()}", ContentMarkdown = "Before." });
        created.ContentMarkdown = "After autosave.";

        var saved = await ExpectSavedAsync(s => s.AutosaveAsync(created.Id, created));

        await Assert.That((await FindAsync(created.Id)).ContentMarkdown).IsEqualTo("After autosave.");
        await Assert.That(saved.PendingChanges).IsNull();
        await Assert.That(await RevisionKindsAsync(created.Id)).Contains(RevisionKind.Autosave);
    }

    /// <summary>
    /// Autosaving a published post leaves the live content unchanged and reports pending changes until
    /// Update applies them (Q3, T1.6).
    /// </summary>
    [Test]
    public async Task Autosave_Published_StagesChangesUntilUpdate()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Staged {Unique()}", ContentMarkdown = "Live content." });
        var published = await PublishAsync(created.Id);
        var liveHtml = (await FindAsync(created.Id)).ContentHtml;

        var draft = Copy(published);
        draft.ContentMarkdown = "Half-finished edit.";
        var autosaved = await ExpectSavedAsync(s => s.AutosaveAsync(created.Id, draft));

        var stored = await FindAsync(created.Id);
        await Assert.That(stored.ContentMarkdown).IsEqualTo("Live content.");
        await Assert.That(stored.ContentHtml).IsEqualTo(liveHtml);
        await Assert.That(autosaved.RowVersion).IsEquivalentTo(published.RowVersion!);
        await Assert.That(autosaved.PendingChanges?.ContentMarkdown).IsEqualTo("Half-finished edit.");
        var reloaded = await WithServiceAsync(s => s.GetPostAsync(created.Id));
        await Assert.That(reloaded!.PendingChanges?.ContentMarkdown).IsEqualTo("Half-finished edit.");

        draft.RowVersion = autosaved.RowVersion;
        var updated = await UpdateAsync(draft);

        await Assert.That((await FindAsync(created.Id)).ContentMarkdown).IsEqualTo("Half-finished edit.");
        await Assert.That(updated.PendingChanges).IsNull();
    }

    /// <summary>Only the newest autosaves are kept per post; manual and publish revisions are untouched.</summary>
    [Test]
    public async Task Autosave_KeepsLatestTwenty()
    {
        var post = await CreateAsync(new PostEditDto { Title = $"Many autosaves {Unique()}" });
        post = await PublishAsync(post.Id);

        for (var i = 0; i < ServerPostAdminService.AutosavesToKeep + 5; i++)
        {
            post.ContentMarkdown = $"Autosave {i}";
            var saved = await ExpectSavedAsync(s => s.AutosaveAsync(post.Id, post));
            post.RowVersion = saved.RowVersion;
        }

        var kinds = await RevisionKindsAsync(post.Id);
        await Assert.That(kinds.Count(k => k == RevisionKind.Autosave)).IsEqualTo(ServerPostAdminService.AutosavesToKeep);
        await Assert.That(kinds).Contains(RevisionKind.Manual);
        await Assert.That(kinds).Contains(RevisionKind.Publish);
        var latest = await WithServiceAsync(s => s.GetPostAsync(post.Id));
        await Assert.That(latest!.PendingChanges?.ContentMarkdown).IsEqualTo($"Autosave {ServerPostAdminService.AutosavesToKeep + 4}");
    }

    /// <summary>Autosaving with a stale row version is a conflict, like any other save.</summary>
    [Test]
    public async Task Autosave_StaleRowVersion_ReturnsConflict()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Stale autosave {Unique()}" });
        var stale = Copy(created);
        await UpdateAsync(created);

        var result = await WithServiceAsync(s => s.AutosaveAsync(stale.Id, stale));

        await Assert.That(result).IsTypeOf<PostConflict>();
    }

    /// <summary>Deleting moves the post to the trash: it disappears from the admin service but the row remains.</summary>
    [Test]
    public async Task Delete_MovesPostToTrash()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Trash {Unique()}" });

        await Assert.That(await WithServiceAsync(s => s.DeleteAsync(created.Id))).IsTrue();

        await Assert.That(await WithServiceAsync(s => s.GetPostAsync(created.Id))).IsNull();
        await Assert.That(await WithServiceAsync(s => s.DeleteAsync(created.Id))).IsFalse();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await dbContext.Posts.IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id);
        await Assert.That(row.IsDeleted).IsTrue();
    }

    /// <summary>The list filters by status, tag and text, and they combine.</summary>
    [Test]
    public async Task GetPosts_FiltersCombine()
    {
        var token = Unique();
        var tag = $"Filter{token}";
        var draft = await CreateAsync(new PostEditDto { Title = $"Filter draft {token}", Tags = [tag] });
        var published = await PublishAsync((await CreateAsync(new PostEditDto { Title = $"Filter published {token}", Tags = [tag] })).Id);
        await CreateAsync(new PostEditDto { Title = $"Filter untagged {token}" });

        var byTag = await WithServiceAsync(s => s.GetPostsAsync(new PostListQuery { Tag = tag.ToLowerInvariant() }));
        var byTagAndStatus = await WithServiceAsync(s => s.GetPostsAsync(new PostListQuery { Tag = tag, Status = PostListStatus.Published }));
        var bySearch = await WithServiceAsync(s => s.GetPostsAsync(new PostListQuery { Search = $"filter DRAFT {token}" }));

        await Assert.That(byTag.Items.Select(p => p.Id)).IsEquivalentTo([draft.Id, published.Id]);
        await Assert.That(byTagAndStatus.Items.Select(p => p.Id)).IsEquivalentTo([published.Id]);
        await Assert.That(byTagAndStatus.Items[0].PublicPath).IsEqualTo(published.PublicPath);
        await Assert.That(bySearch.Items.Select(p => p.Id)).IsEquivalentTo([draft.Id]);
    }

    /// <summary>Paging splits results and reports the total.</summary>
    [Test]
    public async Task GetPosts_Pages()
    {
        var token = Unique();
        for (var i = 0; i < 5; i++)
        {
            await CreateAsync(new PostEditDto { Title = $"Paged {token} {i}" });
        }

        var page2 = await WithServiceAsync(s => s.GetPostsAsync(new PostListQuery { Search = token, Page = 2, PageSize = 2 }));

        await Assert.That(page2.TotalCount).IsEqualTo(5);
        await Assert.That(page2.TotalPages).IsEqualTo(3);
        await Assert.That(page2.Items).Count().IsEqualTo(2);
    }

    /// <summary>The slug check reports taken slugs with the suffix a save would use, and malformed slugs.</summary>
    [Test]
    public async Task CheckSlug_ReportsAvailability()
    {
        var created = await CreateAsync(new PostEditDto { Title = $"Slug check {Unique()}" });

        var taken = await WithServiceAsync(s => s.CheckSlugAsync(new SlugCheckRequest { Slug = created.Slug }));
        var own = await WithServiceAsync(s => s.CheckSlugAsync(new SlugCheckRequest { Slug = created.Slug, PostId = created.Id }));
        var fromTitle = await WithServiceAsync(s => s.CheckSlugAsync(new SlugCheckRequest { Title = created.Title }));
        var malformed = await WithServiceAsync(s => s.CheckSlugAsync(new SlugCheckRequest { Slug = "Not Valid" }));

        await Assert.That(taken.IsAvailable).IsFalse();
        await Assert.That(taken.Suggestion).IsEqualTo(created.Slug + "-2");
        await Assert.That(own.IsAvailable).IsTrue();
        await Assert.That(fromTitle.Slug).IsEqualTo(created.Slug);
        await Assert.That(fromTitle.IsAvailable).IsFalse();
        await Assert.That(malformed.IsValid).IsFalse();
        await Assert.That(malformed.Suggestion).IsEqualTo("not-valid");
    }

    /// <summary>Tag search is case-insensitive, puts prefix matches first and counts usage.</summary>
    [Test]
    public async Task TagSearch_MatchesCaseInsensitively()
    {
        var token = Unique();
        await CreateAsync(new PostEditDto { Title = $"Tag search {token}", Tags = [$"x{token}Rust", $"{token}Go"] });

        await using var scope = factory.Services.CreateAsyncScope();
        var tags = await scope.ServiceProvider.GetRequiredService<ITagService>().SearchAsync(token.ToUpperInvariant());

        await Assert.That(tags.Select(t => t.Name)).IsEquivalentTo([$"{token}Go", $"x{token}Rust"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tags.All(t => t.PostCount == 1)).IsTrue();
    }

    /// <summary>A short random token that keeps each test's titles, slugs and tags apart.</summary>
    private static string Unique()
    {
        return Guid.NewGuid().ToString("N")[..10];
    }

    private static PostEditDto Copy(PostEditDto post)
    {
        return System.Text.Json.JsonSerializer.Deserialize<PostEditDto>(System.Text.Json.JsonSerializer.Serialize(post))!;
    }

    private async Task<T> WithServiceAsync<T>(Func<IPostAdminService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPostAdminService>());
    }

    private async Task<PostEditDto> ExpectSavedAsync(Func<IPostAdminService, Task<PostSaveResult>> action)
    {
        var result = await WithServiceAsync(action);
        return result is PostSaved saved
            ? saved.Post
            : throw new InvalidOperationException($"Expected the save to succeed but got {result}.");
    }

    private Task<PostEditDto> CreateAsync(PostEditDto post)
    {
        return ExpectSavedAsync(s => s.CreateAsync(post));
    }

    private Task<PostEditDto> UpdateAsync(PostEditDto post)
    {
        return ExpectSavedAsync(s => s.UpdateAsync(post.Id, post));
    }

    private Task<PostEditDto> PublishAsync(int id)
    {
        return ExpectSavedAsync(s => s.PublishAsync(id, new PublishPostRequest()));
    }

    private async Task<Post> FindAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Posts.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<bool> IsPubliclyVisibleAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        return await dbContext.Posts.VisibleToPublic(clock).AnyAsync(p => p.Id == id);
    }

    private async Task<List<RevisionKind>> RevisionKindsAsync(int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PostRevisions.Where(r => r.PostId == postId).Select(r => r.Kind).ToListAsync();
    }

    private async Task<string?> RedirectTargetAsync(string fromPath)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Redirects.Where(r => r.FromPath == fromPath).Select(r => r.ToPath).SingleOrDefaultAsync();
    }

    private async Task<int> CountTagsAsync(string name)
    {
        var normalized = BlogEngine.Shared.Text.TagNormalizer.ToNormalizedName(name);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Tags.CountAsync(t => t.NormalizedName == normalized);
    }

    private async Task<MediaItem> AddMediaAsync()
    {
        var publicId = Guid.NewGuid().ToString("N")[..12];
        var item = new MediaItem
        {
            PublicId = publicId,
            FileName = "photo.jpg",
            OriginalStorageKey = $"{publicId}/original.jpg",
            CurrentStorageKey = $"{publicId}/original.jpg",
            ContentType = "image/jpeg",
            Width = 10,
            Height = 10,
            SizeBytes = 100,
            ContentHash = new string('0', 64)
        };

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.MediaItems.Add(item);
        await dbContext.SaveChangesAsync();
        return item;
    }

    private async Task<List<int>> MediaIdsOfAsync(int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PostMedia.Where(pm => pm.PostId == postId).Select(pm => pm.MediaItemId).ToListAsync();
    }
}
