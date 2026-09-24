using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Tests <see cref="PostOutline"/>: the table of contents built from a post's HTML (P13, T4.13).</summary>
public class PostOutlineTests
{
    private const string FourSections = """
        <h2 id="setup"><a href="#setup" class="heading-anchor">Setup</a></h2>
        <h3 id="install">Install</h3>
        <h3 id="configure">Configure</h3>
        <h2 id="usage">Usage</h2>
        <h2 id="pitfalls">Pitfalls</h2>
        <h4 id="deep">Too deep to list</h4>
        <h2 id="wrap-up">Wrap
          up</h2>
        """;

    /// <summary>Four h2 sections give an outline with the h3 headings nested under their section.</summary>
    [Test]
    public async Task FourSections_BuildNestedOutline()
    {
        var outline = PostOutline.FromHtml(FourSections);

        await Assert.That(outline.Select(h => h.Id)).IsEquivalentTo(["setup", "usage", "pitfalls", "wrap-up"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(outline[0].Text).IsEqualTo("Setup");
        await Assert.That(outline[0].Children.Select(h => h.Id)).IsEquivalentTo(["install", "configure"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(outline[1].Children).IsEmpty();
        await Assert.That(outline[3].Text).IsEqualTo("Wrap up");
    }

    /// <summary>Fewer than <see cref="PostOutline.MinimumSections"/> sections need no table of contents.</summary>
    [Test]
    public async Task FewSections_IsEmpty()
    {
        var outline = PostOutline.FromHtml("<h2 id=\"a\">A</h2><h2 id=\"b\">B</h2><h2 id=\"c\">C</h2><h3 id=\"d\">D</h3>");

        await Assert.That(outline).IsEmpty();
    }

    /// <summary>Headings without an id can't be linked to, so they are left out; an h3 before any h2 stands alone.</summary>
    [Test]
    public async Task HeadingsWithoutIds_Skipped_LeadingH3StandsAlone()
    {
        var outline = PostOutline.FromHtml("<h3 id=\"intro\">Intro</h3><h2>No id</h2><h2 id=\"a\">A</h2><h2 id=\"b\">B</h2><h2 id=\"c\">C</h2><h2 id=\"d\">D</h2>");

        await Assert.That(outline.Select(h => h.Id)).IsEquivalentTo(["intro", "a", "b", "c", "d"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>Empty content has no outline.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("<p>Just text.</p>")]
    public async Task NoHeadings_IsEmpty(string? html)
    {
        await Assert.That(PostOutline.FromHtml(html)).IsEmpty();
    }
}
