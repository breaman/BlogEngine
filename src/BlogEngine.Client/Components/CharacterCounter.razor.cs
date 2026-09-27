using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

/// <summary>
/// "42 / 70" under a text field with a length limit, such as the SEO overrides (A16), turning amber near the limit and
/// red past it.
/// </summary>
/// <example>
/// <code>
/// &lt;CharacterCounter Length="@(Post.MetaTitle?.Length ?? 0)" Max="FieldLengths.MetaTitle" /&gt;
/// </code>
/// </example>
public sealed partial class CharacterCounter : ComponentBase
{
    /// <summary>The current length.</summary>
    [Parameter] public int Length { get; set; }

    /// <summary>The limit.</summary>
    [Parameter, EditorRequired] public int Max { get; set; }

    /// <summary>How close to <see cref="Max"/> the counter turns amber.</summary>
    [Parameter] public int Warning { get; set; } = 10;
}