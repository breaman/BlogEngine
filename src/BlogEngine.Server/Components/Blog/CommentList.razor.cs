using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// The approved comments of a post (design 14.2, T3.6): oldest first, with replies grouped under the comment they
/// answer, one level deep (design 6.5).
/// </summary>
public partial class CommentList : ComponentBase
{
    /// <summary>The approved comments, oldest first.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<PublicComment> Comments { get; set; } = [];

    /// <summary>Show Gravatar images instead of colored initials (the "avatars" setting).</summary>
    [Parameter]
    public bool ShowAvatars { get; set; }

    /// <summary>The current time, for relative dates.</summary>
    [Parameter]
    public DateTimeOffset Now { get; set; }

    private IReadOnlyList<CommentThread> _threads = [];

    /// <summary>Groups the comments into threads.</summary>
    protected override void OnParametersSet()
    {
        _threads = Thread(Comments);
    }

    /// <summary>
    /// Puts each reply under its top-level comment, keeping the order of <paramref name="comments"/>. A reply whose
    /// parent isn't shown (not approved) is shown on its own rather than lost.
    /// </summary>
    public static IReadOnlyList<CommentThread> Thread(IReadOnlyList<PublicComment> comments)
    {
        var shown = comments.Select(c => c.Id).ToHashSet();
        var replies = comments
            .Where(c => c.ParentCommentId is { } parent && shown.Contains(parent))
            .ToLookup(c => c.ParentCommentId!.Value);

        return
        [
            .. comments
                .Where(c => c.ParentCommentId is not { } parent || !shown.Contains(parent))
                .Select(c => new CommentThread(c, [.. replies[c.Id]]))
        ];
    }

    /// <summary>A top-level comment and its replies.</summary>
    public sealed record CommentThread(PublicComment Comment, IReadOnlyList<PublicComment> Replies);
}
