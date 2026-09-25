using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Validation;

namespace BlogEngine.UnitTests.Validation;

/// <summary>Tests <see cref="UpdateTagRequestValidator"/>: the rules for renaming a tag in tag management (O3, T4.22).</summary>
public class UpdateTagRequestValidatorTests
{
    private static readonly UpdateTagRequestValidator Validator = new();

    /// <summary>A name is enough; a blank slug means "derive it from the name".</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    public async Task Validate_NameWithBlankSlug_IsValid(string? slug)
    {
        await Assert.That(Validate(new UpdateTagRequest { Name = "C#", Slug = slug })).IsEmpty();
    }

    /// <summary>Names follow the editor's tag rules: not empty, not too long, no delimiters.</summary>
    [Test]
    [Arguments("", "Tag names can't be empty.")]
    [Arguments("a,b", "Tag names can't contain commas or semicolons.")]
    [Arguments("a;b", "Tag names can't contain commas or semicolons.")]
    public async Task Validate_InvalidName_IsRejectedWithTheNormalizersMessage(string name, string message)
    {
        var errors = Validate(new UpdateTagRequest { Name = name });

        await Assert.That(errors.Keys).IsEquivalentTo([nameof(UpdateTagRequest.Name)]);
        await Assert.That(errors[nameof(UpdateTagRequest.Name)].Single()).IsEqualTo(message);
    }

    /// <summary>A name over the limit is rejected.</summary>
    [Test]
    public async Task Validate_LongName_IsRejected()
    {
        var errors = Validate(new UpdateTagRequest { Name = new string('a', FieldLengths.TagName + 1) });

        await Assert.That(errors.Keys).IsEquivalentTo([nameof(UpdateTagRequest.Name)]);
    }

    /// <summary>A typed slug must have the slug shape; surrounding spaces are ignored.</summary>
    [Test]
    [Arguments("csharp", true)]
    [Arguments("c-sharp", true)]
    [Arguments(" c-sharp ", true)]
    [Arguments("C-Sharp", false)]
    [Arguments("c--sharp", false)]
    [Arguments("-csharp", false)]
    [Arguments("c#", false)]
    public async Task Validate_TypedSlug_MustBeWellFormed(string slug, bool valid)
    {
        var errors = Validate(new UpdateTagRequest { Name = "C#", Slug = slug });

        await Assert.That(errors.ContainsKey(nameof(UpdateTagRequest.Slug))).IsEqualTo(!valid);
    }

    /// <summary>Slugs and descriptions have length limits.</summary>
    [Test]
    public async Task Validate_LongSlugAndDescription_AreRejected()
    {
        var errors = Validate(new UpdateTagRequest
        {
            Name = "Tag",
            Slug = new string('a', FieldLengths.TagSlug + 1),
            Description = new string('a', FieldLengths.TagDescription + 1)
        });

        await Assert.That(errors.Keys).IsEquivalentTo([nameof(UpdateTagRequest.Slug), nameof(UpdateTagRequest.Description)]);
    }

    private static IDictionary<string, string[]> Validate(UpdateTagRequest request)
    {
        return Validator.Validate(request).ToDictionary();
    }
}
