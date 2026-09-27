using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

/// <summary>
/// A post's publishing state as a Bootstrap badge: Draft, Scheduled or Published (design 6.3, T4.1).
/// </summary>
/// <remarks>
/// "Scheduled" isn't stored; it is a published post whose date is still ahead, decided here with
/// <see cref="PostSchedule"/> and the injected <see cref="TimeProvider"/>.
/// </remarks>
/// <example>
/// <code>
/// &lt;PostStatusBadge Status="post.Status" PublishedOn="post.PublishedOn" /&gt;
/// </code>
/// </example>
public sealed partial class PostStatusBadge : ComponentBase
{
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>The stored status.</summary>
    [Parameter, EditorRequired] public PostStatus Status { get; set; }

    /// <summary>The publish time, which tells scheduled from published.</summary>
    [Parameter] public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>Extra CSS classes, such as <c>ms-auto</c>.</summary>
    [Parameter] public string? Class { get; set; }

    private string _label = string.Empty;
    private string _cssClass = string.Empty;

    /// <summary>Picks the label and color for the current state.</summary>
    protected override void OnParametersSet()
    {
        (_label, _cssClass) = Status switch
        {
            PostStatus.Published when PostSchedule.IsScheduled(Status, PublishedOn, TimeProvider.GetUtcNow()) => ("Scheduled", "text-bg-info"),
            PostStatus.Published => ("Published", "text-bg-success"),
            _ => ("Draft", "text-bg-secondary")
        };
    }
}