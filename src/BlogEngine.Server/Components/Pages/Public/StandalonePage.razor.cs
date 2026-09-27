using BlogEngine.Server.Services;
using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// A standalone page such as <c>/about</c> (design 6.7, 7.1, A17), at the lowest route priority: every literal route
/// (<c>/posts</c>, <c>/tags</c>, <c>/archive</c>, <c>/search</c>, <c>/admin</c>, …) wins over <c>/{pageSlug}</c>,
/// and those words can't be page slugs (<c>ReservedSlugs</c>).
/// </summary>
/// <remarks>
/// Resolution, like the post page's: a published page answers at its canonical, lowercase path, and other spellings
/// (<c>/About</c>) are redirected there permanently. A path that matches no published page is looked up in the redirect
/// table (a renamed page) before answering 404. The table is checked here rather than left to
/// <see cref="RedirectFallbackMiddleware"/>, because every single-segment path now matches this route, and
/// <c>NavigationManager.NotFound()</c> renders the not-found page straight away.
/// </remarks>
public partial class StandalonePage : ComponentBase
{
    [Inject] private PublicPageQueries Queries { get; set; } = default!;
    [Inject] private RedirectLookup Redirects { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The request, for issuing permanent redirects (always present in static SSR).</summary>
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    /// <summary>The slug from the URL.</summary>
    [Parameter]
    public string PageSlug { get; set; } = string.Empty;

    private PublicPageContent? _page;

    /// <summary>Resolves the URL to a page, a redirect or a 404 (see the class remarks).</summary>
    protected override async Task OnParametersSetAsync()
    {
        _page = null;

        var page = await Queries.GetPageAsync(PageSlug);
        if (page is not null)
        {
            if (HttpContext is { } http && !string.Equals(http.Request.Path.Value, page.Page.Path, StringComparison.Ordinal))
            {
                http.Response.Redirect(page.Page.Path + http.Request.QueryString, permanent: true);
                return;
            }

            _page = page;
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