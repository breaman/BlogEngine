using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.RateLimiting;

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
/// <para>
/// Under the post come the older and newer posts and related posts (P10), then its approved comments and the comment
/// form (design 14.2, T3.3, T3.6). The page's only POST is that form, so the comment rate limit applies to this endpoint
/// (<see cref="CommentRateLimiting"/>; GETs are never limited). The page is never output-cached, because the form carries a per-visitor antiforgery token (design 11).
/// </para>
/// </remarks>
[EnableRateLimiting(CommentRateLimiting.PolicyName)]
public partial class PostDetail : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private RedirectLookup Redirects { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private PublicCommentQueries CommentQueries { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

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

    /// <summary>The comment a reader chose to reply to with its "Reply" link (C6).</summary>
    [SupplyParameterFromQuery(Name = CommentList.ReplyToParameter)]
    public int? ReplyTo { get; set; }

    /// <summary>Related posts shown under a post (design 14.2, P10).</summary>
    private const int RelatedPostCount = 3;

    private PublicPostContent? _post;
    private DateOnly? _updatedDate;
    private PublicPostNeighbors _neighbors = PublicPostNeighbors.None;
    private IReadOnlyList<PublicPostSummary> _related = [];
    private IReadOnlyList<ShareLinks.Link> _shareLinks = [];
    private string _dateFormat = SiteSettingsDefaults.DateFormat;
    private IReadOnlyList<PublicComment> _comments = [];
    private bool _commentsOpen;
    private bool _commentsClosed;
    private bool _showAvatars;
    private DateTimeOffset _now;

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

            var settings = await SettingsService.GetAsync();
            _dateFormat = settings.DateFormat;
            _updatedDate = post.Post.UpdatedDateLocal(settings.TimeZoneId);
            _shareLinks = ShareLinks.For(post.Post.Title, NavigationManager.ToAbsoluteUri(post.Post.Path).AbsoluteUri);

            // Previous/next and related posts come from the cached snapshot the post itself was found in (P10).
            var index = await Queries.GetIndexAsync();
            _neighbors = index.GetNeighbors(post.Post.Id);
            _related = index.GetRelated(post.Post, RelatedPostCount);

            // Comments (design 8.5): the site-wide switch hides the form everywhere; a post can also close its own.
            _now = TimeProvider.GetUtcNow();
            _comments = (await CommentQueries.GetApprovedAsync(post.Post.Id)).Comments;
            _commentsOpen = settings.CommentsEnabled && post.CommentsOpenAt(_now);
            _commentsClosed = settings.CommentsEnabled && !_commentsOpen;
            _showAvatars = settings.ShowCommentAvatars;
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
