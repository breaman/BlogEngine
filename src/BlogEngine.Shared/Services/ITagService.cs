using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Tag lookups for the admin area (design 6.4, 7.4).
/// </summary>
public interface ITagService
{
    /// <summary>Default and maximum number of autocomplete suggestions.</summary>
    const int MaxSuggestions = 10;

    /// <summary>
    /// Autocomplete: tags whose name contains <paramref name="search"/>, case-insensitively, with prefix
    /// matches first and then the most used. A blank search returns the most used tags.
    /// </summary>
    Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default);
}
