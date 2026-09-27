using System.Globalization;

using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// A private preview of a post at <c>/preview/{token}</c> (design 6.7, 7.1, A14, T4.4): the saved content of a draft,
/// scheduled or published post, with a "Preview" banner, for anyone holding an unexpired link.
/// </summary>
/// <remarks>
/// <para>
/// Unknown, expired and revoked tokens, and tokens of posts in the trash, are a 404, the same as any missing page, so
/// a guess reveals nothing.
/// </para>
/// <para>
/// Every response sends <c>X-Robots-Tag: noindex</c> (plus the meta tag) so a leaked link is never indexed,
/// <c>Referrer-Policy: no-referrer</c> so the token doesn't leak to sites the post links to, and
/// <c>Cache-Control: no-store</c> so a revoked link isn't served from a cache. <c>robots.txt</c> also disallows
/// <c>/preview</c>. The page has no comment form and isn't output-cached.
/// </para>
/// </remarks>
public partial class PostPreview : ComponentBase
{
    [Inject] private PreviewPostQuery Query { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The request, for the privacy headers (always present in static SSR).</summary>
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    /// <summary>The preview token from the URL.</summary>
    [Parameter]
    public string Token { get; set; } = string.Empty;

    private PreviewPost? _post;
    private string _html = string.Empty;
    private IReadOnlyList<OutlineHeading> _outline = [];
    private string _expiresText = string.Empty;
    private string? _scheduledText;

    /// <summary>Sets the privacy headers, then shows the post or a 404.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _post = null;
        if (HttpContext is { } http)
        {
            http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.Headers.CacheControl = "no-store";
        }

        var post = await Query.FindAsync(Token);
        if (post is null)
        {
            NavigationManager.NotFound();
            return;
        }

        var settings = await SettingsService.GetAsync();
        _expiresText = FormatInBlogTime(post.ExpiresOn, settings.TimeZoneId, settings.DateFormat, includeTime: false);
        _scheduledText = post.ScheduledFor is { } scheduledFor
            ? FormatInBlogTime(scheduledFor, settings.TimeZoneId, settings.DateFormat, includeTime: true)
            : null;

        // Like the post page: in-page links need this page's path (FragmentLinks), and long posts get a table of contents.
        _html = FragmentLinks.Resolve(post.Html, SitePaths.Preview(Token));
        _outline = PostOutline.FromHtml(post.Html);
        _post = post;
    }

    /// <summary>A timestamp in the blog's time zone and date format, falling back to UTC for an unknown zone.</summary>
    private static string FormatInBlogTime(DateTimeOffset instant, string timeZoneId, string dateFormat, bool includeTime)
    {
        DateTime local;
        try
        {
            local = BlogTimeZone.ToLocalDateTime(instant, timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            local = instant.UtcDateTime;
        }

        var date = PublicDates.Format(DateOnly.FromDateTime(local), dateFormat);
        return includeTime ? string.Create(CultureInfo.CurrentCulture, $"{date} at {local:t}") : date;
    }
}