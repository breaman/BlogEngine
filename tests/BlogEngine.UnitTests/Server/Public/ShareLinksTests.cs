using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="ShareLinks"/>: plain share links with the post title and URL escaped (design 14.2).
/// </summary>
public class ShareLinksTests
{
    /// <summary>Each network gets a link carrying the escaped URL, and no share link runs script.</summary>
    [Test]
    public async Task For_EscapesTitleAndUrl()
    {
        const string url = "https://blog.example/posts/2026/09/22/hello";

        var links = ShareLinks.For("C# & you?", url);

        await Assert.That(links.Select(l => l.Network)).IsEquivalentTo(["Mastodon", "Bluesky", "LinkedIn", "Email"]);
        await Assert.That(links.Single(l => l.Network == "LinkedIn").Href)
            .IsEqualTo("https://www.linkedin.com/sharing/share-offsite/?url=https%3A%2F%2Fblog.example%2Fposts%2F2026%2F09%2F22%2Fhello");
        await Assert.That(links.Single(l => l.Network == "Bluesky").Href).Contains("text=C%23%20%26%20you%3F%20https%3A%2F%2F");
        await Assert.That(links.Single(l => l.Network == "Email").Href).StartsWith("mailto:?subject=C%23%20%26%20you%3F&body=https%3A");
        await Assert.That(links.All(l => l.Href.StartsWith("https://", StringComparison.Ordinal) || l.Href.StartsWith("mailto:", StringComparison.Ordinal))).IsTrue();
    }
}