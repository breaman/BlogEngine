using System.ComponentModel.DataAnnotations;

using BlogEngine.Shared.Common;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One social profile link shown in the site footer and author bio, such as GitHub or Mastodon.
/// </summary>
public sealed class SocialLinkDto
{
    /// <summary>Display name of the network, for example <c>GitHub</c>.</summary>
    [Required]
    [MaxLength(FieldLengths.SocialNetwork)]
    public string Network { get; set; } = string.Empty;

    /// <summary>Absolute URL of the profile.</summary>
    [Required]
    [Url]
    [MaxLength(FieldLengths.Url)]
    public string Url { get; set; } = string.Empty;
}
