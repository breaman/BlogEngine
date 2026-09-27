using BlogEngine.Shared.Contracts;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

/// <summary>
/// A post editor field for one media library image outside the content, such as the cover (A15) or the social image
/// (A16): a thumbnail with Change and Remove, or a Choose button while it is empty.
/// </summary>
/// <remarks>Choosing is left to the page, which opens <see cref="MediaPicker.PickAsync"/>.</remarks>
/// <example>
/// <code>
/// &lt;PostImageField Image="Post.CoverImage" ChooseText="Choose cover image" OnChoose="ChooseCoverAsync" OnRemove="RemoveCoverAsync" /&gt;
/// </code>
/// </example>
public sealed partial class PostImageField : ComponentBase
{
    /// <summary>The chosen image, or <see langword="null"/> when there is none.</summary>
    [Parameter] public PostImageDto? Image { get; set; }

    /// <summary>The label of the button shown while no image is chosen.</summary>
    [Parameter] public string ChooseText { get; set; } = "Choose image";

    /// <summary>Disables the buttons, for example while prerendered.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Raised by Choose and Change.</summary>
    [Parameter] public EventCallback OnChoose { get; set; }

    /// <summary>Raised by Remove.</summary>
    [Parameter] public EventCallback OnRemove { get; set; }
}