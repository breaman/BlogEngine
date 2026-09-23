using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// The public feeds (design 7.1, 16, P6, T1.24): <c>/feed.xml</c> (RSS 2.0), <c>/atom.xml</c> (Atom 1.0) and
/// <c>/tags/{slug}/feed.xml</c> (RSS 2.0 for one tag), each with the latest
/// <see cref="PublicPostQueries.FeedItemCount"/> visible posts.
/// </summary>
/// <remarks>
/// Responses are output-cached under <see cref="PublicCacheTags.Posts"/>, which <see cref="CacheInvalidator"/>
/// evicts whenever a post or the settings change; drafts never appear because the items come from
/// <see cref="PublicPostQueries"/>.
/// </remarks>
public static class FeedEndpoints
{
    /// <summary>Maps the feed endpoints.</summary>
    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(SitePaths.RssFeed, (HttpRequest request, PublicPostQueries queries, ISettingsService settings,
                SyndicationFeedWriter writer, CancellationToken ct) =>
            WriteSiteFeedAsync(SyndicationFeedWriter.Format.Rss, request, queries, settings, writer, ct))
            .CacheOutput(PublicOutputCachePolicies.Posts);

        endpoints.MapGet(SitePaths.AtomFeed, (HttpRequest request, PublicPostQueries queries, ISettingsService settings,
                SyndicationFeedWriter writer, CancellationToken ct) =>
            WriteSiteFeedAsync(SyndicationFeedWriter.Format.Atom, request, queries, settings, writer, ct))
            .CacheOutput(PublicOutputCachePolicies.Posts);

        endpoints.MapGet($"{TagPaths.Index}/{{slug}}/feed.xml", WriteTagFeedAsync)
            .CacheOutput(PublicOutputCachePolicies.Posts);

        return endpoints;
    }

    /// <summary>Writes the site-wide feed in <paramref name="format"/>.</summary>
    private static async Task<FileContentHttpResult> WriteSiteFeedAsync(SyndicationFeedWriter.Format format, HttpRequest request,
        PublicPostQueries queries, ISettingsService settingsService, SyndicationFeedWriter writer, CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var posts = await queries.GetFeedAsync(tagId: null, cancellationToken);
        var siteUrl = PublicSiteUrl.Root(request);
        var path = format == SyndicationFeedWriter.Format.Atom ? SitePaths.AtomFeed : SitePaths.RssFeed;

        var xml = writer.Write(format, settings, posts, siteUrl, new Uri(siteUrl, path));
        return TypedResults.File(xml, format == SyndicationFeedWriter.Format.Atom
            ? SyndicationFeedWriter.AtomContentType
            : SyndicationFeedWriter.RssContentType);
    }

    /// <summary>Writes the RSS feed of one tag; 404 when the tag has no visible posts.</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> WriteTagFeedAsync(string slug, HttpRequest request,
        PublicPostQueries queries, ISettingsService settingsService, SyndicationFeedWriter writer, CancellationToken cancellationToken)
    {
        if (await queries.GetTagAsync(slug, cancellationToken) is not { } tag)
        {
            return TypedResults.NotFound();
        }

        var settings = await settingsService.GetAsync(cancellationToken);
        var posts = await queries.GetFeedAsync(tag.Id, cancellationToken);
        var siteUrl = PublicSiteUrl.Root(request);

        var xml = writer.Write(SyndicationFeedWriter.Format.Rss, settings, posts, siteUrl, new Uri(siteUrl, TagPaths.Feed(tag.Slug)), tag);
        return TypedResults.File(xml, SyndicationFeedWriter.RssContentType);
    }
}
