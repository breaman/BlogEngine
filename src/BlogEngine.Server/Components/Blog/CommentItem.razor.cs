using System.Globalization;

using BlogEngine.Server.Services.Comments;
using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// One approved comment on the post page (design 14.2, T3.6).
/// </summary>
/// <remarks>
/// The body is the sanitized HTML stored at submit time (<see cref="CommentRenderer"/>), so it is rendered as markup.
/// </remarks>
public partial class CommentItem : ComponentBase
{
    /// <summary>The <c>rel</c> of the author's website link: no ranking credit, and no access to this page.</summary>
    protected const string LinkRel = CommentHtmlSanitizer.LinkRel;

    /// <summary>The comment.</summary>
    [Parameter, EditorRequired]
    public PublicComment Comment { get; set; } = default!;

    /// <summary>Show a Gravatar image instead of a colored initial.</summary>
    [Parameter]
    public bool ShowAvatars { get; set; }

    /// <summary>The current time, for the relative date.</summary>
    [Parameter]
    public DateTimeOffset Now { get; set; }

    /// <summary>Where the "Reply" link goes (C6); no link when <see langword="null"/> (comments are closed).</summary>
    [Parameter]
    public string? ReplyHref { get; set; }

    private string IsoDate => Comment.CreatedOn.ToString("O", CultureInfo.InvariantCulture);

    private string FullDate => Comment.CreatedOn.ToUniversalTime().ToString("MMMM d, yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
