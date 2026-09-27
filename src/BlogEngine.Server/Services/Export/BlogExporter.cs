using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using BlogEngine.Data.Models;
using BlogEngine.Server.Storage;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Export;

/// <summary>
/// Writes the whole blog as a zip (design 17, O7): posts and pages as Markdown with YAML front matter, the media library's
/// originals with their metadata, and the comments, tags, redirects and settings as JSON.
/// </summary>
/// <remarks>
/// <para>Layout of the archive:</para>
/// <code>
/// posts/2026-09-22-building-a-blog-engine.md   (a draft that was never published: posts/{slug}.md)
/// pages/about.md
/// media/{publicId}/original.jpg
/// media/{publicId}/metadata.json               (alt text, caption, edit operations, …)
/// comments.json
/// tags.json
/// redirects.json
/// settings.json
/// </code>
/// <para>
/// The front matter follows the Hugo and Jekyll conventions (<c>title</c>, <c>date</c>, <c>lastmod</c>, <c>tags</c>,
/// <c>summary</c>, <c>draft</c>) so the content is portable, plus <c>slug</c>, <c>status</c> and <c>cover</c> so an
/// import can rebuild the same URLs. Posts in the trash are left out.
/// </para>
/// <para>
/// The zip is written to the output one entry at a time, through a <see cref="ZipOutputSpool"/> that holds each entry only
/// until it has been passed on asynchronously (the zip library flushes entries with synchronous writes, which ASP.NET Core
/// rejects). Memory use is bounded by the largest entry, not the size of the library. Media is stored uncompressed, since
/// images are compressed already.
/// </para>
/// </remarks>
public sealed partial class BlogExporter(
    ApplicationDbContext dbContext,
    ISettingsService settingsService,
    IMediaStorage storage,
    TimeProvider timeProvider,
    ILogger<BlogExporter> logger)
{
    /// <summary>JSON options for the metadata files: camelCase, indented, enums by name, readable non-ASCII text.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>UTF-8 without a byte order mark, which static site generators expect.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The download's file name, <c>blog-export-{yyyy-MM-dd}.zip</c>, dated in the blog's time zone.</summary>
    public async Task<string> GetFileNameAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var today = BlogTimeZone.ToLocalDate(timeProvider.GetUtcNow(), settings.TimeZoneId);

        return $"blog-export-{today:yyyy-MM-dd}.zip";
    }

    /// <summary>Writes the export zip to <paramref name="output"/>, which is left open.</summary>
    public async Task WriteAsync(Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        var settings = await settingsService.GetAsync(cancellationToken);
        await using var spool = new ZipOutputSpool();
        var zip = await ZipArchive.CreateAsync(spool, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        var archive = new ExportArchive(zip, spool, output);

        var posts = await WritePostsAsync(archive, cancellationToken);
        var pages = await WritePagesAsync(archive, cancellationToken);
        var media = await WriteMediaAsync(archive, cancellationToken);
        var comments = await WriteCommentsAsync(archive, cancellationToken);
        await WriteTagsAsync(archive, cancellationToken);
        await WriteRedirectsAsync(archive, cancellationToken);
        await archive.WriteJsonAsync("settings.json", settings, cancellationToken);

        // Disposing the archive writes the central directory into the spool; it goes out with the last drain.
        await zip.DisposeAsync();
        await spool.DrainToAsync(output, cancellationToken);

        BlogExported(logger, posts, pages, media, comments);
    }

    /// <summary>Writes one Markdown file per post outside the trash; returns how many.</summary>
    private async Task<int> WritePostsAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var posts = await dbContext.Posts
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Title,
                p.Slug,
                p.Summary,
                p.ContentMarkdown,
                p.Status,
                p.PublishedOn,
                p.PublishedDateLocal,
                p.LastUpdatedOn,
                p.CreatedOn,
                p.IsFeatured,
                p.AllowComments,
                p.MetaTitle,
                p.MetaDescription,
                Cover = p.CoverMedia == null ? null : new { p.CoverMedia.PublicId, p.CoverMedia.FileName },
                SocialImage = p.SocialImageMedia == null ? null : new { p.SocialImageMedia.PublicId, p.SocialImageMedia.FileName },
                Tags = p.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var post in posts)
        {
            var status = PostSchedule.IsScheduled(post.Status, post.PublishedOn, now) ? "scheduled" : StatusName(post.Status);
            var markdown = new FrontMatterWriter()
                .Add("title", post.Title)
                .Add("slug", post.Slug)
                .Add("date", post.PublishedOn ?? post.CreatedOn)
                .Add("lastmod", post.LastUpdatedOn)
                .Add("tags", post.Tags)
                .Add("summary", post.Summary)
                .Add("status", status)
                .Add("draft", post.Status == PostStatus.Draft)
                .Add("featured", post.IsFeatured)
                .Add("allowComments", post.AllowComments)
                .Add("cover", post.Cover is null ? null : MediaPaths.Item(post.Cover.PublicId, post.Cover.FileName))
                .Add("socialImage", post.SocialImage is null ? null : MediaPaths.Item(post.SocialImage.PublicId, post.SocialImage.FileName))
                .Add("metaTitle", post.MetaTitle)
                .Add("metaDescription", post.MetaDescription)
                .ToDocument(post.ContentMarkdown);

            // Dated like Jekyll's _posts; a post that was never published has no date to put in its name.
            var fileName = post.PublishedDateLocal is { } date ? $"{date:yyyy-MM-dd}-{post.Slug}.md" : $"{post.Slug}.md";
            await archive.WriteTextAsync($"posts/{fileName}", markdown, cancellationToken);
        }

        return posts.Count;
    }

    /// <summary>Writes one Markdown file per standalone page; returns how many.</summary>
    private async Task<int> WritePagesAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        var pages = await dbContext.Pages
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new { p.Title, p.Slug, p.Summary, p.ContentMarkdown, p.Status, p.ShowInNav, p.NavOrder, p.MetaTitle, p.MetaDescription })
            .ToListAsync(cancellationToken);

        foreach (var page in pages)
        {
            var markdown = new FrontMatterWriter()
                .Add("title", page.Title)
                .Add("slug", page.Slug)
                .Add("summary", string.IsNullOrEmpty(page.Summary) ? null : page.Summary)
                .Add("status", StatusName(page.Status))
                .Add("draft", page.Status == PostStatus.Draft)
                .Add("showInNav", page.ShowInNav)
                .Add("navOrder", page.NavOrder)
                .Add("metaTitle", page.MetaTitle)
                .Add("metaDescription", page.MetaDescription)
                .ToDocument(page.ContentMarkdown);

            await archive.WriteTextAsync($"pages/{page.Slug}.md", markdown, cancellationToken);
        }

        return pages.Count;
    }

    /// <summary>
    /// Copies every library item's original upload with a <c>metadata.json</c> next to it; returns how many items were
    /// exported. An item whose original is missing from storage still gets its metadata (with <c>originalMissing</c>), so
    /// the export shows what was lost rather than failing as a whole.
    /// </summary>
    private async Task<int> WriteMediaAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        var items = await dbContext.MediaItems
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                m.PublicId,
                m.FileName,
                m.OriginalStorageKey,
                m.ContentType,
                m.Width,
                m.Height,
                m.SizeBytes,
                m.AltText,
                m.Caption,
                m.ContentHash,
                m.Version,
                m.EditOperationsJson,
                m.CreatedOn
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var folder = $"media/{item.PublicId}";
            var originalName = "original" + Path.GetExtension(item.OriginalStorageKey);

            var copied = false;
            await using (var source = await storage.OpenReadAsync(item.OriginalStorageKey, cancellationToken))
            {
                if (source is not null)
                {
                    await archive.WriteStreamAsync($"{folder}/{originalName}", source, cancellationToken);
                    copied = true;
                }
            }

            if (!copied)
            {
                logger.LogWarning("The original of media item {PublicId} ({StorageKey}) is missing from storage; only its metadata was exported.",
                    item.PublicId, item.OriginalStorageKey);
            }

            var metadata = new MediaMetadata(
                item.PublicId,
                item.FileName,
                MediaPaths.Item(item.PublicId, item.FileName),
                copied ? originalName : null,
                copied ? null : true,
                item.ContentType,
                item.Width,
                item.Height,
                item.SizeBytes,
                item.AltText,
                item.Caption,
                item.ContentHash,
                item.Version,
                ParseEditOperations(item.EditOperationsJson),
                item.CreatedOn);
            await archive.WriteJsonAsync($"{folder}/metadata.json", metadata, cancellationToken);
        }

        return items.Count;
    }

    /// <summary>Writes every comment on a post outside the trash, oldest first; returns how many.</summary>
    private async Task<int> WriteCommentsAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        // IP hashes are left out: they are keyed with this installation's secret, so they mean nothing anywhere else.
        var comments = await dbContext.Comments
            .AsNoTracking()
            .OrderBy(c => c.CreatedOn)
            .ThenBy(c => c.Id)
            .Select(c => new CommentExport(
                c.Id,
                c.Post.Slug,
                c.ParentCommentId,
                c.AuthorName,
                c.AuthorEmail,
                c.AuthorUrl,
                c.BodyMarkdown,
                c.Status,
                c.IsAuthorReply,
                c.CreatedOn))
            .ToListAsync(cancellationToken);

        await archive.WriteJsonAsync("comments.json", comments, cancellationToken);
        return comments.Count;
    }

    /// <summary>Writes the tags with their slugs and descriptions, which the posts' front matter can't carry.</summary>
    private async Task WriteTagsAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        var tags = await dbContext.Tags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TagExport(t.Name, t.Slug, t.Description))
            .ToListAsync(cancellationToken);

        await archive.WriteJsonAsync("tags.json", tags, cancellationToken);
    }

    /// <summary>Writes the redirects, so old URLs can keep working on a new site.</summary>
    private async Task WriteRedirectsAsync(ExportArchive archive, CancellationToken cancellationToken)
    {
        var redirects = await dbContext.Redirects
            .AsNoTracking()
            .OrderBy(r => r.FromPath)
            .Select(r => new RedirectExport(r.FromPath, r.ToPath, r.StatusCode))
            .ToListAsync(cancellationToken);

        await archive.WriteJsonAsync("redirects.json", redirects, cancellationToken);
    }

    /// <summary>
    /// The zip being written, with the spool it writes into and the real output: every entry is drained to the output as
    /// soon as it is closed (see <see cref="ZipOutputSpool"/>).
    /// </summary>
    private sealed class ExportArchive(ZipArchive zip, ZipOutputSpool spool, Stream output)
    {
        /// <summary>Adds a UTF-8 text entry.</summary>
        public Task WriteTextAsync(string name, string text, CancellationToken cancellationToken)
        {
            return WriteEntryAsync(name, CompressionLevel.Optimal, stream => stream.WriteAsync(Utf8.GetBytes(text), cancellationToken).AsTask(),
                cancellationToken);
        }

        /// <summary>Adds an indented JSON entry.</summary>
        public Task WriteJsonAsync<T>(string name, T value, CancellationToken cancellationToken)
        {
            return WriteEntryAsync(name, CompressionLevel.Optimal, stream => JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken),
                cancellationToken);
        }

        /// <summary>Adds an entry copied from <paramref name="source"/>, stored without compression (images are compressed already).</summary>
        public Task WriteStreamAsync(string name, Stream source, CancellationToken cancellationToken)
        {
            return WriteEntryAsync(name, CompressionLevel.NoCompression, stream => source.CopyToAsync(stream, cancellationToken), cancellationToken);
        }

        private async Task WriteEntryAsync(string name, CompressionLevel level, Func<Stream, Task> write, CancellationToken cancellationToken)
        {
            var entry = zip.CreateEntry(name, level);
            await using (var stream = await entry.OpenAsync(cancellationToken))
            {
                await write(stream);
            }

            await spool.DrainToAsync(output, cancellationToken);
        }
    }

    /// <summary>The stored edit operations as JSON, or <see langword="null"/> when there are none or they can't be read.</summary>
    private static JsonElement? ParseEditOperations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StatusName(PostStatus status)
    {
        return status switch
        {
            PostStatus.Published => "published",
            _ => "draft"
        };
    }

    [LoggerMessage(EventId = 6001, EventName = "BlogExported", Level = LogLevel.Information,
        Message = "Blog exported: {PostCount} posts, {PageCount} pages, {MediaCount} media items, {CommentCount} comments")]
    private static partial void BlogExported(ILogger logger, int postCount, int pageCount, int mediaCount, int commentCount);

    /// <summary>A media item's <c>metadata.json</c>.</summary>
    /// <param name="Width">Width of the current (edited) version, in pixels.</param>
    /// <param name="Height">Height of the current (edited) version, in pixels.</param>
    /// <param name="EditOperations">The crop, rotation and resize applied to the original to make the current version.</param>
    private sealed record MediaMetadata(
        string PublicId,
        string FileName,
        string Path,
        string? Original,
        bool? OriginalMissing,
        string ContentType,
        int Width,
        int Height,
        long SizeBytes,
        string AltText,
        string? Caption,
        string ContentHash,
        int Version,
        JsonElement? EditOperations,
        DateTimeOffset? CreatedOn);

    /// <summary>One entry of <c>comments.json</c>; <paramref name="ParentId"/> is the <paramref name="Id"/> of the comment replied to.</summary>
    private sealed record CommentExport(
        int Id,
        string PostSlug,
        int? ParentId,
        string AuthorName,
        string AuthorEmail,
        string? AuthorUrl,
        string BodyMarkdown,
        CommentStatus Status,
        bool IsAuthorReply,
        DateTimeOffset? CreatedOn);

    /// <summary>One entry of <c>tags.json</c>.</summary>
    private sealed record TagExport(string Name, string Slug, string? Description);

    /// <summary>One entry of <c>redirects.json</c>.</summary>
    private sealed record RedirectExport(string FromPath, string ToPath, int StatusCode);
}