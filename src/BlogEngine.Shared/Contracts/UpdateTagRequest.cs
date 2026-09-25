using BlogEngine.Shared.Validation;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Renames a tag or changes its slug or description (<c>PUT /api/admin/tags/{id}</c>, design 6.4, O3). The rules are in
/// <see cref="UpdateTagRequestValidator"/>.
/// </summary>
public sealed class UpdateTagRequest
{
    /// <summary>The new display name; cleaned and normalized like a name typed in the editor.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The new slug, or blank to derive it from <see cref="Name"/>. Changing the slug redirects the old tag page and feed to
    /// the new ones.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>Optional description shown on the public tag page; blank removes it.</summary>
    public string? Description { get; set; }
}
