using BlogEngine.Shared.Validation;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One social profile link shown in the site footer and author bio, such as GitHub or Mastodon.
/// </summary>
/// <remarks>Validated by <see cref="SocialLinkValidator"/>.</remarks>
public sealed class SocialLinkDto
{
    /// <summary>Display name of the network, for example <c>GitHub</c>.</summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>Absolute URL of the profile.</summary>
    public string Url { get; set; } = string.Empty;
}