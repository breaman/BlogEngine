using BlogEngine.Server.Services;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// The post page at <c>/posts/{yyyy}/{mm}/{dd}/{slug}</c> (design 7.1, 14.2, P1).
/// </summary>
/// <remarks>
/// Resolution, in order (design 7.1):
/// <list type="number">
/// <item>Find the visible post by slug. If the requested path isn't exactly its canonical path (the date was
/// edited, the month or day isn't zero-padded, the slug has capitals, there's a trailing slash), answer
/// 301 to the canonical URL.</item>
/// <item>Otherwise look the path up in the redirect table (moved posts) and answer with its 301.</item>
/// <item>Otherwise 404. Drafts, scheduled and trashed posts never get past the first step, because the
/// lookup only sees visible posts.</item>
/// </list>
/// The redirect table is checked here rather than left to <see cref="RedirectFallbackMiddleware"/>, because
/// <c>NavigationManager.NotFound()</c> renders the not-found page straight away in static SSR.
/// </remarks>
public partial class PostDetail : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private RedirectLookup Redirects { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The request, for issuing permanent redirects (always present in static SSR).</summary>
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    /// <summary>The year from the URL.</summary>
    [Parameter]
    public int Year { get; set; }

    /// <summary>The month from the URL.</summary>
    [Parameter]
    public int Month { get; set; }

    /// <summary>The day from the URL.</summary>
    [Parameter]
    public int Day { get; set; }

    /// <summary>The slug from the URL.</summary>
    [Parameter]
    public string Slug { get; set; } = string.Empty;

    private PublicPostContent? _post;
    private IReadOnlyList<ShareLinks.Link> _shareLinks = [];
    private string _dateFormat = SiteSettingsDefaults.DateFormat;

    /// <summary>Resolves the URL to a post, a redirect or a 404 (see the class remarks).</summary>
    protected override async Task OnParametersSetAsync()
    {
        _post = null;

        var post = await Queries.GetPostAsync(Slug);
        if (post is not null)
        {
            // The page renders nothing while redirecting; browsers (and enhanced navigation) follow the header.
            if (HttpContext is { } http && !string.Equals(http.Request.Path.Value, post.Post.Path, StringComparison.Ordinal))
            {
                http.Response.Redirect(post.Post.Path + http.Request.QueryString, permanent: true);
                return;
            }

            _dateFormat = (await SettingsService.GetAsync()).DateFormat;
            _shareLinks = ShareLinks.For(post.Post.Title, NavigationManager.ToAbsoluteUri(post.Post.Path).AbsoluteUri);
            _post = post;
            return;
        }

        if (HttpContext is { } context
            && await Redirects.FindAsync(context.Request.Path, context.Request.QueryString) is { } redirect)
        {
            context.Response.StatusCode = redirect.StatusCode;
            context.Response.Headers.Location = redirect.Location;
            return;
        }

        NavigationManager.NotFound();
    }
}
