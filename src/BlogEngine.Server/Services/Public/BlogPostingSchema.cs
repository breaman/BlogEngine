using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Builds the schema.org <c>BlogPosting</c> JSON-LD of a post page (design 14.2, P8), which search engines read for
/// rich results: headline, dates, author, image and keywords.
/// </summary>
/// <remarks>
/// <para>
/// The JSON is written into a <c>&lt;script type="application/ld+json"&gt;</c> element as raw markup, so it is serialized
/// with an encoder that escapes the HTML-sensitive characters (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, quotes): a title
/// containing <c>&lt;/script&gt;</c> can't end the element early. Other Unicode stays readable.
/// </para>
/// <para>
/// The author is a <c>Person</c> named by the author name setting, or, when none is set, the blog itself as an
/// <c>Organization</c>. Dates are UTC ISO 8601.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var json = BlogPostingSchema.ToJson(new BlogPostingSchema.Data(url, article, "Why I built it", image, "Ada", "My blog", siteUrl));
/// </code>
/// </example>
public static class BlogPostingSchema
{
    /// <summary>Longest headline Google shows for articles; longer titles are shortened with an ellipsis.</summary>
    public const int MaxHeadlineLength = 110;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // JavaScriptEncoder always escapes <, >, &, ' and "; allowing every range only keeps other text unescaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>The inputs of a <c>BlogPosting</c>.</summary>
    /// <param name="Url">The post's absolute canonical URL.</param>
    /// <param name="Article">Headline, dates and tags.</param>
    /// <param name="Description">The meta description.</param>
    /// <param name="ImageUrl">The absolute URL of the social image, if any.</param>
    /// <param name="AuthorName">The author name setting; the site is the author when it is blank.</param>
    /// <param name="SiteTitle">The site title.</param>
    /// <param name="SiteUrl">The site's absolute root URL.</param>
    /// <param name="AuthorImageUrl">The absolute URL of the author avatar setting, if any; used only for a <c>Person</c> author.</param>
    public sealed record Data(
        Uri Url,
        SeoArticle Article,
        string? Description,
        string? ImageUrl,
        string? AuthorName,
        string SiteTitle,
        Uri SiteUrl,
        string? AuthorImageUrl = null);

    /// <summary>The JSON-LD document, safe to embed in a <c>&lt;script&gt;</c> element.</summary>
    public static string ToJson(Data data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var site = data.SiteUrl.AbsoluteUri;
        var url = data.Url.AbsoluteUri;
        var article = data.Article;

        var node = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["mainEntityOfPage"] = new JsonObject { ["@type"] = "WebPage", ["@id"] = url },
            ["url"] = url,
            ["headline"] = Shorten(article.Headline, MaxHeadlineLength),
            ["datePublished"] = Iso(article.PublishedOn),
            ["dateModified"] = Iso(article.ModifiedOn > article.PublishedOn ? article.ModifiedOn : article.PublishedOn),
            ["author"] = string.IsNullOrWhiteSpace(data.AuthorName)
                ? new JsonObject { ["@type"] = "Organization", ["name"] = data.SiteTitle, ["url"] = site }
                : Person(data.AuthorName.Trim(), site, data.AuthorImageUrl),
            ["isPartOf"] = new JsonObject { ["@type"] = "Blog", ["name"] = data.SiteTitle, ["url"] = site }
        };

        if (!string.IsNullOrWhiteSpace(data.Description))
        {
            node["description"] = data.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(data.ImageUrl))
        {
            node["image"] = new JsonArray(data.ImageUrl);
        }

        if (article.Tags.Count > 0)
        {
            node["keywords"] = string.Join(", ", article.Tags);
        }

        return node.ToJsonString(SerializerOptions);
    }

    /// <summary>The <c>Person</c> author, with the avatar as its <c>image</c> when one is set.</summary>
    private static JsonObject Person(string name, string site, string? imageUrl)
    {
        var person = new JsonObject { ["@type"] = "Person", ["name"] = name, ["url"] = site };
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            person["image"] = imageUrl;
        }

        return person;
    }

    /// <summary>ISO 8601 in UTC, such as <c>2026-09-22T17:30:00Z</c>.</summary>
    private static string Iso(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Cuts <paramref name="text"/> to <paramref name="max"/> characters at a word boundary, adding an ellipsis.</summary>
    private static string Shorten(string text, int max)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= max)
        {
            return trimmed;
        }

        var cut = trimmed[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
}
