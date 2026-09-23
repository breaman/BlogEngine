namespace BlogEngine.Data.Models;

/// <summary>
/// One social profile link in <see cref="SiteSettings.SocialLinks"/>. Stored inside the settings row
/// as JSON (an EF Core complex type), not in its own table.
/// </summary>
public class SocialLink
{
    /// <summary>Display name of the network, for example <c>GitHub</c>.</summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>Absolute URL of the profile.</summary>
    public string Url { get; set; } = string.Empty;
}
