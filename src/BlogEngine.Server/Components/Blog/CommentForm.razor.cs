using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// The public comment form on the post page (design 5.1, 8.1, C1, T3.3).
/// </summary>
/// <remarks>
/// <para>
/// Static SSR: the form posts back to the post page (<see cref="FormName"/> picks this handler), Blazor binds the fields
/// with <see cref="SupplyParameterFromFormAttribute"/>, validates them with the shared
/// <c>CommentSubmissionValidator</c> through Blazilla, and <see cref="CommentSubmissionService"/> does the rest. It works
/// with JavaScript disabled; with JavaScript, the enhanced form post avoids a full page load.
/// </para>
/// <para>
/// Two extra fields feed the spam guard (design 8.3): an empty honeypot (<see cref="HoneypotField"/>) and the render
/// time signed with Data Protection (<see cref="TimestampField"/>). After a submission the commenter always sees the
/// same message, whatever the spam guard decided; a pending comment is never shown back (design 8.4).
/// </para>
/// </remarks>
public partial class CommentForm : ComponentBase
{
    /// <summary>Handler name of the form; unique on the post page.</summary>
    public const string FormName = "comment";

    /// <summary>Name of the honeypot field: innocuous, so bots fill it in.</summary>
    public const string HoneypotField = "website2";

    /// <summary>Name of the hidden field holding the signed render time.</summary>
    public const string TimestampField = "ts";

    [Inject] private CommentSubmissionService Submissions { get; set; } = default!;
    [Inject] private CommentFormTimestamp FormTimestamp { get; set; } = default!;
    [Inject] private CommentIpHasher IpHasher { get; set; } = default!;

    /// <summary>The request, for the commenter's IP address and user agent (always present in static SSR).</summary>
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    /// <summary>The post being commented on.</summary>
    [Parameter, EditorRequired]
    public int PostId { get; set; }

    /// <summary>The post's canonical path, which the form posts to.</summary>
    [Parameter, EditorRequired]
    public string PostPath { get; set; } = string.Empty;

    /// <summary>The submitted fields; a blank comment when the page is first shown.</summary>
    [SupplyParameterFromForm(FormName = FormName)]
    private CommentSubmission Input { get; set; } = default!;

    /// <summary>The honeypot as submitted.</summary>
    [SupplyParameterFromForm(FormName = FormName, Name = HoneypotField)]
    private string? Honeypot { get; set; }

    /// <summary>The signed render time as submitted.</summary>
    [SupplyParameterFromForm(FormName = FormName, Name = TimestampField)]
    private string? Timestamp { get; set; }

    private string _renderToken = string.Empty;
    private StatusMessage? _status;

    /// <summary>Prepares a blank form and signs the time it is rendered at.</summary>
    protected override void OnInitialized()
    {
        Input ??= new CommentSubmission();
        _renderToken = FormTimestamp.Create(PostId);
    }

    /// <summary>Submits a valid form and replaces it with a blank one and a message.</summary>
    private async Task SubmitAsync()
    {
        var context = new CommentSubmissionContext(
            IpHasher.Hash(HttpContext?.Connection.RemoteIpAddress),
            HttpContext?.Request.Headers.UserAgent.ToString(),
            Honeypot,
            Timestamp);

        var result = await Submissions.SubmitAsync(PostId, Input, context);
        _status = result.Outcome switch
        {
            CommentSubmitOutcome.AwaitingModeration => new("Thanks! Your comment is awaiting moderation.", "alert-success"),
            CommentSubmitOutcome.Published => new("Thanks! Your comment has been published; reload the page to see it.", "alert-success"),
            CommentSubmitOutcome.Closed or CommentSubmitOutcome.NotFound => new("Comments are closed.", "alert-warning"),
            _ => new(string.Join(" ", result.Errors.SelectMany(e => e.Value)), "alert-danger")
        };

        if (result.Outcome is CommentSubmitOutcome.AwaitingModeration or CommentSubmitOutcome.Published)
        {
            Input = new CommentSubmission();
        }
    }

    /// <summary>A message shown above the form after a submission.</summary>
    private sealed record StatusMessage(string Message, string CssClass);
}
