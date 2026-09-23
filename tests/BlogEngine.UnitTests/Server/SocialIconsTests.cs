using BlogEngine.Server.Components.Layout;

namespace BlogEngine.UnitTests.Server;

/// <summary>
/// Tests <see cref="SocialIcons"/>: footer icons chosen from the network names typed in the settings.
/// </summary>
public class SocialIconsTests
{
    /// <summary>Known networks get their brand icon, matched case-insensitively and by substring.</summary>
    [Test]
    [Arguments("GitHub", "bi-github")]
    [Arguments(" mastodon ", "bi-mastodon")]
    [Arguments("Bluesky", "bi-bluesky")]
    [Arguments("My LinkedIn", "bi-linkedin")]
    [Arguments("X", "bi-twitter-x")]
    [Arguments("Twitter", "bi-twitter-x")]
    [Arguments("Email", "bi-envelope")]
    public async Task For_KnownNetwork_ReturnsBrandIcon(string network, string expected)
    {
        await Assert.That(SocialIcons.For(network)).IsEqualTo(expected);
    }

    /// <summary>Anything else, including names that merely contain an "x", gets the generic link icon.</summary>
    [Test]
    [Arguments("My website")]
    [Arguments("Codeberg")]
    [Arguments("Xbox")]
    [Arguments("")]
    [Arguments(null)]
    public async Task For_UnknownNetwork_ReturnsFallback(string? network)
    {
        await Assert.That(SocialIcons.For(network)).IsEqualTo(SocialIcons.Fallback);
    }
}
