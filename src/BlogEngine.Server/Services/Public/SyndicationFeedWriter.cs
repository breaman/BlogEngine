using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Builds the RSS 2.0 and Atom 1.0 feeds (design 16, T1.24) with <c>System.ServiceModel.Syndication</c>.
/// </summary>
/// <remarks>
/// <para>
/// Items carry the full sanitized HTML (or only the summary, per the <see cref="FeedContentMode"/> setting) with
/// every URL made absolute, because feed readers show it away from the site. Tags become categories.
/// </para>
/// <para>
/// RSS puts the HTML in <c>&lt;description&gt;</c>, which is what readers expect; Atom puts it in
/// <c>&lt;content type="html"&gt;</c> with the plain summary alongside. Atom also requires an author, so the
/// feed-level author is the author name from the settings, or the site title when none is set.
/// </para>
/// </remarks>
public sealed class SyndicationFeedWriter(PostHtmlSanitizer sanitizer)
{
    /// <summary>Media type of the RSS feeds.</summary>
    public const string RssContentType = "application/rss+xml; charset=utf-8";

    /// <summary>Media type of the Atom feed.</summary>
    public const string AtomContentType = "application/atom+xml; charset=utf-8";

    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    /// <summary>The feed format.</summary>
    public enum Format
    {
        /// <summary>RSS 2.0.</summary>
        Rss,

        /// <summary>Atom 1.0.</summary>
        Atom
    }

    /// <summary>Serializes a feed of <paramref name="posts"/> as UTF-8 XML.</summary>
    /// <param name="format">RSS or Atom.</param>
    /// <param name="settings">The site settings, for the title, description, author and content mode.</param>
    /// <param name="posts">The posts, newest first.</param>
    /// <param name="siteUrl">The site's absolute root URL, such as <c>https://blog.example/</c>.</param>
    /// <param name="feedUrl">The absolute URL of this feed, for the self link.</param>
    /// <param name="tag">The tag, for a tag's feed.</param>
    public byte[] Write(Format format, SiteSettingsDto settings, IReadOnlyList<PublicPostContent> posts, Uri siteUrl,
        Uri feedUrl, PublicTag? tag = null)
    {
        var feed = Build(format, settings, posts, siteUrl, feedUrl, tag);

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            SyndicationFeedFormatter formatter = format == Format.Atom ? new Atom10FeedFormatter(feed) : new Rss20FeedFormatter(feed);
            formatter.WriteTo(writer);
        }

        return stream.ToArray();
    }

    /// <summary>Builds the feed object model.</summary>
    private SyndicationFeed Build(Format format, SiteSettingsDto settings, IReadOnlyList<PublicPostContent> posts,
        Uri siteUrl, Uri feedUrl, PublicTag? tag)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(posts);

        var title = tag is null ? settings.SiteTitle : $"{settings.SiteTitle}: {tag.Name}";
        var description = tag?.Description ?? settings.Description ?? settings.Tagline ?? settings.SiteTitle;
        var alternate = tag is null ? siteUrl : new Uri(siteUrl, tag.Path);

        var feed = new SyndicationFeed(title, description, alternate)
        {
            Id = alternate.AbsoluteUri,
            Language = "en",
            Generator = "BlogEngine",
            // An empty feed has no posts to date it; fall back to the Unix epoch so repeated requests agree.
            LastUpdatedTime = posts.Count > 0 ? posts.Max(p => p.Post.LastModified) : DateTimeOffset.UnixEpoch,
            Items = [.. posts.Select(p => BuildItem(format, settings.FeedContentMode, p, siteUrl))]
        };
        feed.Links.Add(SyndicationLink.CreateSelfLink(feedUrl, format == Format.Atom ? "application/atom+xml" : "application/rss+xml"));

        if (format == Format.Rss)
        {
            // RSS carries its self link and dates as Atom extension elements. Declaring the conventional "atom"
            // prefix on the root makes them <atom:link> rather than .NET's default <a10:link>, which the W3C
            // validator warns about.
            feed.AttributeExtensions.Add(new XmlQualifiedName("atom", "http://www.w3.org/2000/xmlns/"), AtomNamespace);
        }

        if (format == Format.Atom)
        {
            feed.Authors.Add(new SyndicationPerson { Name = string.IsNullOrWhiteSpace(settings.AuthorName) ? settings.SiteTitle : settings.AuthorName });
        }

        return feed;
    }

    /// <summary>Builds one item: absolute link and id, dates, categories and the content.</summary>
    private SyndicationItem BuildItem(Format format, FeedContentMode mode, PublicPostContent content, Uri siteUrl)
    {
        var post = content.Post;
        var url = new Uri(siteUrl, post.Path);

        var item = new SyndicationItem
        {
            Id = url.AbsoluteUri,
            Title = new TextSyndicationContent(post.Title),
            PublishDate = post.PublishedOn,
            LastUpdatedTime = post.LastModified
        };
        item.Links.Add(SyndicationLink.CreateAlternateLink(url));
        foreach (var tag in post.Tags)
        {
            item.Categories.Add(new SyndicationCategory(tag.Name));
        }

        var html = mode == FeedContentMode.FullContent ? sanitizer.Sanitize(content.Html, url) : null;
        if (format == Format.Atom)
        {
            item.Summary = new TextSyndicationContent(post.Summary);
            if (html is not null)
            {
                item.Content = SyndicationContent.CreateHtmlContent(html);
            }
        }
        else
        {
            item.Summary = html is not null
                ? new TextSyndicationContent(html, TextSyndicationContentKind.Html)
                : new TextSyndicationContent(post.Summary);
        }

        return item;
    }
}