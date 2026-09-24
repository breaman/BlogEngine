using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services.Public;
using BlogEngine.UnitTests.Server.Public;

using Bunit;

namespace BlogEngine.UnitTests.Server.Components;

/// <summary>
/// Component tests for the parts of the post page added in Phase 4 (design 14.2): the "Updated" date in
/// <see cref="PostMeta"/> (P15), <see cref="TableOfContents"/> (P13), <see cref="PostNavigation"/> (P10), and the
/// <see cref="Pager"/> labels used by search results (P9).
/// </summary>
public class PostPageComponentsTests
{
    /// <summary>The updated date follows the publish date in the site's date format.</summary>
    [Test]
    public async Task PostMeta_ShowsUpdatedDate()
    {
        await using var context = new BunitContext();

        var cut = context.Render<PostMeta>(p => p
            .Add(x => x.Post, PublicTestData.Post(1, new DateOnly(2026, 9, 2)))
            .Add(x => x.DateFormat, "d MMM yyyy")
            .Add(x => x.UpdatedDate, new DateOnly(2026, 9, 30)));

        var updated = cut.Find(".post-updated");
        await Assert.That(updated.TextContent).IsEqualTo("Updated 30 Sep 2026");
        await Assert.That(updated.QuerySelector("time")!.GetAttribute("datetime")).IsEqualTo("2026-09-30");
    }

    /// <summary>Without an updated date there is no "Updated" part.</summary>
    [Test]
    public async Task PostMeta_NoUpdatedDate_ShowsNone()
    {
        await using var context = new BunitContext();

        var cut = context.Render<PostMeta>(p => p.Add(x => x.Post, PublicTestData.Post(1)));

        await Assert.That(cut.FindAll(".post-updated")).IsEmpty();
    }

    /// <summary>Entries link to the page's path plus the heading id, with h3 entries nested.</summary>
    [Test]
    public async Task TableOfContents_LinksToHeadingsOnPage()
    {
        await using var context = new BunitContext();
        IReadOnlyList<OutlineHeading> outline =
        [
            new("setup", "Setup", [new("install", "Install", [])]),
            new("usage", "Usage", [])
        ];

        var cut = context.Render<TableOfContents>(p => p.Add(x => x.Entries, outline).Add(x => x.PagePath, "/posts/2026/09/22/hello"));

        var links = cut.FindAll("nav.post-toc a").Select(a => a.GetAttribute("href") ?? "");
        await Assert.That(links).IsEquivalentTo(
            ["/posts/2026/09/22/hello#setup", "/posts/2026/09/22/hello#install", "/posts/2026/09/22/hello#usage"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(cut.Find("nav.post-toc > ol > li > ol > li a").TextContent).IsEqualTo("Install");
    }

    /// <summary>An empty outline renders nothing.</summary>
    [Test]
    public async Task TableOfContents_Empty_RendersNothing()
    {
        await using var context = new BunitContext();

        var cut = context.Render<TableOfContents>(p => p.Add(x => x.Entries, []).Add(x => x.PagePath, "/about"));

        await Assert.That(cut.Markup.Trim()).IsEmpty();
    }

    /// <summary>Both neighbors are linked with prev/next, and related posts are listed.</summary>
    [Test]
    public async Task PostNavigation_LinksNeighborsAndRelated()
    {
        await using var context = new BunitContext();
        var older = PublicTestData.Post(1, new DateOnly(2026, 9, 1));
        var newer = PublicTestData.Post(3, new DateOnly(2026, 9, 3));
        var related = PublicTestData.Post(7, new DateOnly(2026, 8, 7));

        var cut = context.Render<PostNavigation>(p => p
            .Add(x => x.Neighbors, new PublicPostNeighbors(newer, older))
            .Add(x => x.Related, [related]));

        await Assert.That(cut.Find("a[rel=prev]").GetAttribute("href")).IsEqualTo(older.Path);
        await Assert.That(cut.Find("a[rel=next]").GetAttribute("href")).IsEqualTo(newer.Path);
        await Assert.That(cut.Find(".related-posts a").GetAttribute("href")).IsEqualTo(related.Path);
    }

    /// <summary>At the ends only one side is linked, and with no related posts there is no related section.</summary>
    [Test]
    public async Task PostNavigation_Boundary_OmitsMissingParts()
    {
        await using var context = new BunitContext();

        var newest = context.Render<PostNavigation>(p => p.Add(x => x.Neighbors, new PublicPostNeighbors(null, PublicTestData.Post(1))));
        var only = context.Render<PostNavigation>(p => p.Add(x => x.Neighbors, PublicPostNeighbors.None));

        await Assert.That(newest.FindAll("a[rel=next]")).IsEmpty();
        await Assert.That(newest.FindAll("a[rel=prev]").Count).IsEqualTo(1);
        await Assert.That(newest.FindAll(".related-posts")).IsEmpty();
        await Assert.That(only.Markup.Trim()).IsEmpty();
    }

    /// <summary>Search results page with "Previous"/"Next" and keep their query string.</summary>
    [Test]
    public async Task Pager_CustomLabels_AndQueryBasePath()
    {
        await using var context = new BunitContext();

        var cut = context.Render<Pager>(p => p
            .Add(x => x.Page, 2).Add(x => x.TotalPages, 3).Add(x => x.BasePath, "/search?q=blazor")
            .Add(x => x.PreviousText, "Previous").Add(x => x.NextText, "Next"));

        await Assert.That(cut.Find("a[rel=prev]").TextContent).Contains("Previous");
        await Assert.That(cut.Find("a[rel=prev]").GetAttribute("href")).IsEqualTo("/search?q=blazor");
        await Assert.That(cut.Find("a[rel=next]").GetAttribute("href")).IsEqualTo("/search?q=blazor&page=3");
    }
}
