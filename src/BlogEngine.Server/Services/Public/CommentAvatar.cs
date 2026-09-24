using System.Globalization;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// What stands next to a commenter's name (design 8.1): a colored initial by default, which needs no third-party
/// request, or a Gravatar image with an identicon fallback when the "show avatars" setting is on.
/// </summary>
/// <example>
/// <code>
/// CommentAvatar.Initial("  émile"); // "É"
/// CommentAvatar.ColorClass(comment.AvatarHash); // "text-bg-success"
/// </code>
/// </example>
public static class CommentAvatar
{
    /// <summary>Pixel size the Gravatar image is requested at (twice the displayed size, for sharp high-DPI screens).</summary>
    public const int GravatarSize = 80;

    /// <summary>Bootstrap color pairs the initials rotate through; all have readable text on their background.</summary>
    private static readonly string[] Colors =
    [
        "text-bg-primary",
        "text-bg-success",
        "text-bg-danger",
        "text-bg-warning",
        "text-bg-info",
        "text-bg-secondary",
        "text-bg-dark"
    ];

    /// <summary>The first letter or digit of the name, upper-cased, or <c>?</c> when there is none.</summary>
    public static string Initial(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (char.IsLetterOrDigit(element, 0))
            {
                return element.ToUpperInvariant();
            }
        }

        return "?";
    }

    /// <summary>
    /// A background color class picked from the avatar hash, so the same commenter always gets the same color and
    /// the email itself never reaches the page.
    /// </summary>
    public static string ColorClass(string? avatarHash)
    {
        var seed = avatarHash is { Length: >= 2 }
            && int.TryParse(avatarHash.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

        return Colors[seed % Colors.Length];
    }

    /// <summary>The Gravatar image URL for the hash, falling back to a generated identicon.</summary>
    public static string GravatarUrl(string avatarHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(avatarHash);

        return string.Create(CultureInfo.InvariantCulture, $"https://www.gravatar.com/avatar/{avatarHash}?s={GravatarSize}&d=identicon");
    }
}
