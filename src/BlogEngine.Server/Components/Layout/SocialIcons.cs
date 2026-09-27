namespace BlogEngine.Server.Components.Layout;

/// <summary>
/// Picks the Bootstrap Icons class for a social link from the network name the author typed in the settings.
/// </summary>
/// <example>
/// <code>
/// SocialIcons.For("GitHub");     // "bi-github"
/// SocialIcons.For("My website"); // "bi-link-45deg"
/// </code>
/// </example>
public static class SocialIcons
{
    /// <summary>Icon used for networks without a brand icon.</summary>
    public const string Fallback = "bi-link-45deg";

    // Matched as substrings, in order. "X" is only matched as the whole name (in For), since the letter
    // appears in many other names.
    private static readonly (string Keyword, string Icon)[] Icons =
    [
        ("github", "bi-github"),
        ("mastodon", "bi-mastodon"),
        ("bluesky", "bi-bluesky"),
        ("linkedin", "bi-linkedin"),
        ("youtube", "bi-youtube"),
        ("instagram", "bi-instagram"),
        ("facebook", "bi-facebook"),
        ("threads", "bi-threads"),
        ("twitter", "bi-twitter-x"),
        ("email", "bi-envelope"),
        ("mail", "bi-envelope")
    ];

    /// <summary>The icon class for <paramref name="network"/>, matched case-insensitively.</summary>
    public static string For(string? network)
    {
        var name = network?.Trim() ?? string.Empty;
        if (name.Equals("x", StringComparison.OrdinalIgnoreCase))
        {
            return "bi-twitter-x";
        }

        foreach (var (keyword, icon) in Icons)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return icon;
            }
        }

        return Fallback;
    }
}