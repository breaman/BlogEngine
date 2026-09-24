using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the public layout and shared blog components (design 5.1, 14, T1.18): public pages are plain static
/// SSR with no WebAssembly, and the layout shows the site identity, navigation, search box, footer and SEO head.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PublicLayoutTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>
    /// No public page renders a WebAssembly component (which is what would start the runtime) or preloads the
    /// runtime's files. The import map still lists them, but an import map entry downloads nothing by itself.
    /// </summary>
    [Test]
    public async Task PublicPages_LoadNoWebAssembly()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"No wasm {token}", Tags = [$"Nowasm-{token}"] },
            PublicTestPosts.Noon(year, 2, 2));
        using var client = IdentityTestHelper.CreateClient(factory);

        foreach (var path in new[] { "/", "/posts", "/tags", $"/tags/nowasm-{token}", post.PublicPath!, PostPaths.Day(new DateOnly(year, 2, 2)), SitePaths.Archive, "/search?q=wasm" })
        {
            var html = await PublicTestPosts.GetOkAsync(client, path);

            await Assert.That(html).DoesNotContain("\"type\":\"webassembly\"").Because(path);
            await Assert.That(html).DoesNotContain("<!--Blazor:").Because(path);
            await Assert.That(html).DoesNotContain("rel=\"preload\"").Because(path);
            await Assert.That(html).DoesNotContain("rel=\"modulepreload\"").Because(path);
            await Assert.That(html).DoesNotContain(".wasm").Because(path);
        }
    }

    /// <summary>Every public page has the navigation, the GET search form, the footer feed link and feed autodiscovery.</summary>
    [Test]
    public async Task PublicPages_HaveLayoutAndSeoHead()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, "/posts");

        await Assert.That(html).Contains("href=\"/posts\"");
        await Assert.That(html).Contains("href=\"/tags\"");
        await Assert.That(html).Contains("action=\"/search\" method=\"get\"");
        await Assert.That(html).Contains("name=\"q\"");
        await Assert.That(html).Contains("<footer class=\"site-footer");
        await Assert.That(html).Contains("<link rel=\"canonical\" href=\"http://localhost/posts\"");
        // Razor encodes the "+" of the media types as &#x2B;, so match the rest of the links.
        await Assert.That(html).Contains("(RSS)\" href=\"http://localhost/feed.xml\"");
        await Assert.That(html).Contains("(Atom)\" href=\"http://localhost/atom.xml\"");
    }

    /// <summary>
    /// The theme (P14, T4.14) is applied by a blocking script in the head, before the stylesheets, so a dark page never
    /// flashes light; the theme menu and the Archive link are in the navigation.
    /// </summary>
    [Test]
    public async Task Layout_AppliesThemeBeforePaint_AndHasThemeMenu()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, "/posts");
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var themeScript = head.IndexOf("<script src=\"js/theme.", StringComparison.Ordinal);

        await Assert.That(themeScript).IsGreaterThan(-1);
        await Assert.That(head[themeScript..]).DoesNotStartWith("<script src=\"js/theme.js\" type=\"module\"");
        await Assert.That(themeScript).IsLessThan(head.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal));
        await Assert.That(html).Contains("class=\"nav-item dropdown theme-toggle\"");
        await Assert.That(html).Contains("data-theme-value=\"dark\"");
        await Assert.That(html).Contains("href=\"/archive\"");
    }
}
