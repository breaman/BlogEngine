using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Text;

namespace BlogEngine.Client.Components;

/// <summary>
/// Decides what happens when the author commits a tag in <see cref="TagInput"/> (design 6.4, A4, T1.12).
/// </summary>
/// <remarks>
/// Uses the same <see cref="TagNormalizer"/> rules as the server, so <c>c#</c>, <c>C#</c> and <c> c# </c> are one
/// tag: typing a name that matches an existing tag produces that tag's original casing, and a name that
/// matches a chip already on the post is rejected as a duplicate. Kept free of UI code so it can be unit tested.
/// </remarks>
/// <example>
/// <code>
/// var result = TagInputRules.Resolve("c#", current: ["Blazor"], known: [new TagDto { Name = "C#" }], maxTags: 20);
/// // result.Name == "C#", result.MatchedExisting == true
/// </code>
/// </example>
public static class TagInputRules
{
    /// <summary>Resolves typed text to the chip to add, or the reason it can't be added.</summary>
    /// <param name="typed">Text from the input box.</param>
    /// <param name="current">Tags already on the post.</param>
    /// <param name="known">Existing tags to match against, such as the current autocomplete suggestions.</param>
    /// <param name="maxTags">Most tags a post may have.</param>
    public static TagCommitResult Resolve(string? typed, IEnumerable<string> current, IEnumerable<TagDto> known, int maxTags)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(known);

        var normalized = TagNormalizer.Normalize(typed);
        if (!normalized.IsValid)
        {
            return TagCommitResult.Rejected(normalized.Error!);
        }

        var currentList = current as IReadOnlyCollection<string> ?? [.. current];
        if (currentList.FirstOrDefault(tag => TagNormalizer.ToNormalizedName(tag) == normalized.NormalizedName) is { } duplicate)
        {
            return TagCommitResult.Rejected($"'{duplicate}' is already added.");
        }

        if (currentList.Count >= maxTags)
        {
            return TagCommitResult.Rejected($"A post can have at most {maxTags} tags.");
        }

        var existing = known.FirstOrDefault(tag => TagNormalizer.ToNormalizedName(tag.Name) == normalized.NormalizedName);

        return existing is null
            ? new TagCommitResult(normalized.Name, null, MatchedExisting: false)
            : new TagCommitResult(existing.Name, null, MatchedExisting: true);
    }
}

/// <summary>Outcome of <see cref="TagInputRules.Resolve"/>.</summary>
/// <param name="Name">The chip to add, with an existing tag's casing when one matched; <see langword="null"/> when rejected.</param>
/// <param name="Error">Why the tag can't be added, or <see langword="null"/>.</param>
/// <param name="MatchedExisting">Whether <paramref name="Name"/> came from an existing tag.</param>
public sealed record TagCommitResult(string? Name, string? Error, bool MatchedExisting)
{
    /// <summary>Whether the tag can be added.</summary>
    public bool IsValid => Error is null;

    /// <summary>A rejected commit.</summary>
    public static TagCommitResult Rejected(string error)
    {
        return new TagCommitResult(null, error, MatchedExisting: false);
    }
}
