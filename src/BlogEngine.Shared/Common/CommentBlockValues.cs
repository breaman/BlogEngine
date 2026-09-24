using System.Text.RegularExpressions;

using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Common;

/// <summary>
/// Puts blocklist values (design 6.5, 8.3) into the one form they are stored and compared in, so
/// <c>Spam@Example.com </c> and <c>spam@example.com</c> are the same block and the unique index can't be dodged.
/// </summary>
/// <example>
/// <code>
/// CommentBlockValues.Normalize(CommentBlockKind.Domain, " @Spam.Example. ");   // "spam.example"
/// CommentBlockValues.Normalize(CommentBlockKind.Keyword, "  Cheap   PILLS ");  // "cheap pills"
/// </code>
/// </example>
public static partial class CommentBlockValues
{
    /// <summary>
    /// Returns the stored form of <paramref name="value"/>: trimmed and lowercased; runs of whitespace in keywords
    /// collapsed to one space; a leading <c>@</c> and trailing dot removed from domains.
    /// </summary>
    public static string Normalize(CommentBlockKind kind, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();

        return kind switch
        {
            CommentBlockKind.Keyword => Whitespace().Replace(trimmed, " "),
            CommentBlockKind.Domain => trimmed.TrimStart('@').TrimEnd('.'),
            _ => trimmed
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
