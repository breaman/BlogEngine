using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Validation;

namespace BlogEngine.UnitTests.Validation;

/// <summary>
/// Tests <see cref="PageEditValidator"/> and <see cref="ReservedSlugs"/>: the shared standalone page rules (A17, T4.10).
/// </summary>
public class PageEditValidatorTests
{
    private static readonly PageEditValidator Validator = new();

    /// <summary>A title is all a page needs; the slug and summary are generated when blank.</summary>
    [Test]
    public async Task Validate_MinimalPage_IsValid()
    {
        await Assert.That(Validate(new PageEditDto { Title = "About" })).IsEmpty();
    }

    /// <summary>The title is required.</summary>
    [Test]
    public async Task Validate_BlankTitle_IsRejected()
    {
        await Assert.That(Validate(new PageEditDto { Title = " " }).Keys).IsEquivalentTo([nameof(PageEditDto.Title)]);
    }

    /// <summary>Words the site's routes use can't be page slugs, whatever their case.</summary>
    [Test]
    [Arguments("posts")]
    [Arguments("tags")]
    [Arguments("archive")]
    [Arguments("search")]
    [Arguments("admin")]
    [Arguments("api")]
    [Arguments("media")]
    [Arguments("preview")]
    [Arguments("account")]
    [Arguments("setup")]
    public async Task Validate_ReservedSlug_IsRejected(string slug)
    {
        var errors = Validate(new PageEditDto { Title = "Page", Slug = slug });

        await Assert.That(errors.Keys).IsEquivalentTo([nameof(PageEditDto.Slug)]);
        await Assert.That(errors[nameof(PageEditDto.Slug)].Single()).Contains($"'{slug}' is used by the site itself");
        await Assert.That(ReservedSlugs.IsReserved(slug.ToUpperInvariant())).IsTrue();
    }

    /// <summary>Ordinary slugs pass, and the slug format is the same as for posts.</summary>
    [Test]
    [Arguments("about", true)]
    [Arguments("now", true)]
    [Arguments("posts-i-like", true)]
    [Arguments("About", false)]
    [Arguments("about me", false)]
    public async Task Validate_SlugFormat(string slug, bool valid)
    {
        var errors = Validate(new PageEditDto { Title = "Page", Slug = slug });

        await Assert.That(errors.Count == 0).IsEqualTo(valid);
        await Assert.That(ReservedSlugs.IsReserved(slug)).IsFalse();
    }

    /// <summary>The navigation order stays within its range.</summary>
    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(PageEditValidator.MaxNavOrder, true)]
    [Arguments(PageEditValidator.MaxNavOrder + 1, false)]
    public async Task Validate_NavOrderRange(int order, bool valid)
    {
        var errors = Validate(new PageEditDto { Title = "Page", NavOrder = order });

        await Assert.That(errors.Count == 0).IsEqualTo(valid);
    }

    /// <summary>Lengths come from <see cref="FieldLengths"/>.</summary>
    [Test]
    public async Task Validate_EnforcesMaxLengths()
    {
        var errors = Validate(new PageEditDto
        {
            Title = new string('a', FieldLengths.PostTitle + 1),
            Summary = new string('a', FieldLengths.PostSummary + 1),
            MetaTitle = new string('a', FieldLengths.MetaTitle + 1),
            MetaDescription = new string('a', FieldLengths.MetaDescription + 1)
        });

        await Assert.That(errors.Keys).IsEquivalentTo(
            [nameof(PageEditDto.Title), nameof(PageEditDto.Summary), nameof(PageEditDto.MetaTitle), nameof(PageEditDto.MetaDescription)]);
    }

    private static IDictionary<string, string[]> Validate(PageEditDto page)
    {
        return Validator.Validate(page).ToDictionary();
    }
}