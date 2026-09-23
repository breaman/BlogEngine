using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services.Public;
using BlogEngine.UnitTests.Server.Public;

using Bunit;

namespace BlogEngine.UnitTests.Server.Components;

/// <summary>
/// Component tests for the shared public blog components (design 14.1, T1.18): <see cref="Pager"/>,
/// <see cref="Breadcrumbs"/>, <see cref="PostCard"/> and <see cref="TagBadge"/>.
/// </summary>
public class BlogComponentsTests
{
    /// <summary>A single page needs no pager.</summary>
    [Test]
    public async Task Pager_SinglePage_RendersNothing()
    {
        await using var context = new BunitContext();

        var cut = context.Render<Pager>(p => p.Add(x => x.Page, 1).Add(x => x.TotalPages, 1).Add(x => x.BasePath, "/posts"));

        await Assert.That(cut.Markup.Trim()).IsEmpty();
    }

    /// <summary>A middle page links to the newer page (page 1 without a query string) and the older page.</summary>
    [Test]
    public async Task Pager_MiddlePage_LinksBothWays()
    {
        await using var context = new BunitContext();

        var cut = context.Render<Pager>(p => p.Add(x => x.Page, 2).Add(x => x.TotalPages, 3).Add(x => x.BasePath, "/tags/csharp"));

        await Assert.That(cut.Find("a[rel=prev]").GetAttribute("href")).IsEqualTo("/tags/csharp");
        await Assert.That(cut.Find("a[rel=next]").GetAttribute("href")).IsEqualTo("/tags/csharp?page=3");
        await Assert.That(cut.Markup).Contains("Page 2 of 3");
    }

    /// <summary>The last page has no older link, and the first page no newer link.</summary>
    [Test]
    public async Task Pager_Ends_OmitUnavailableLinks()
    {
        await using var context = new BunitContext();

        var first = context.Render<Pager>(p => p.Add(x => x.Page, 1).Add(x => x.TotalPages, 2).Add(x => x.BasePath, "/posts"));
        var last = context.Render<Pager>(p => p.Add(x => x.Page, 2).Add(x => x.TotalPages, 2).Add(x => x.BasePath, "/posts"));

        await Assert.That(first.FindAll("a[rel=prev]")).IsEmpty();
        await Assert.That(first.Find("a[rel=next]").GetAttribute("href")).IsEqualTo("/posts?page=2");
        await Assert.That(last.FindAll("a[rel=next]")).IsEmpty();
    }

    /// <summary>Every item links except the last, which is marked as the current page.</summary>
    [Test]
    public async Task Breadcrumbs_LinkParentsAndMarkCurrent()
    {
        await using var context = new BunitContext();

        var cut = context.Render<Breadcrumbs>(p => p.Add(x => x.Items,
            [new BreadcrumbItem("Home", "/"), new BreadcrumbItem("Posts", "/posts"), new BreadcrumbItem("2026", "/posts/2026")]));

        var items = cut.FindAll("li");
        await Assert.That(items.Count).IsEqualTo(3);
        await Assert.That(items[1].QuerySelector("a")!.GetAttribute("href")).IsEqualTo("/posts");
        await Assert.That(items[2].QuerySelector("a")).IsNull();
        await Assert.That(items[2].GetAttribute("aria-current")).IsEqualTo("page");
    }

    /// <summary>A card links the title, formats the date with the setting, and links each tag.</summary>
    [Test]
    public async Task PostCard_RendersTitleMetaAndTags()
    {
        await using var context = new BunitContext();
        var post = PublicTestData.Post(5, new DateOnly(2026, 9, 2), new PublicTagLink(1, "C#", "csharp"), new PublicTagLink(2, ".NET", "dotnet"))
            with { IsFeatured = true };

        var cut = context.Render<PostCard>(p => p.Add(x => x.Post, post).Add(x => x.DateFormat, "d MMM yyyy").Add(x => x.ShowFeatured, true));

        await Assert.That(cut.Find("h2 a").GetAttribute("href")).IsEqualTo("/posts/2026/09/02/post-5");
        await Assert.That(cut.Find("time").TextContent).IsEqualTo("2 Sep 2026");
        await Assert.That(cut.Find("time").GetAttribute("datetime")).IsEqualTo("2026-09-02");
        await Assert.That(cut.Markup).Contains("3 min read");
        await Assert.That(cut.FindAll("a.tag-badge").Select(a => a.GetAttribute("href") ?? "")).IsEquivalentTo(["/tags/csharp", "/tags/dotnet"]);
        await Assert.That(cut.Markup).Contains("Featured");
        await Assert.That(cut.Markup).Contains("Summary 5.");
    }

    /// <summary>A tag badge shows the count when given one.</summary>
    [Test]
    public async Task TagBadge_ShowsCount()
    {
        await using var context = new BunitContext();

        var cut = context.Render<TagBadge>(p => p.Add(x => x.Name, "C#").Add(x => x.Slug, "csharp").Add(x => x.Count, 4));

        await Assert.That(cut.Find("a").GetAttribute("href")).IsEqualTo("/tags/csharp");
        await Assert.That(cut.Find("a").TextContent).IsEqualTo("#C#4");
    }
}
