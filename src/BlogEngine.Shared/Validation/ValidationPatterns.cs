namespace BlogEngine.Shared.Validation;

/// <summary>
/// Regular expressions shared by the FluentValidation validators on the client and server.
/// </summary>
public static class ValidationPatterns
{
    /// <summary>
    /// A slug: lowercase ASCII letters and digits in hyphen-separated words, with no leading, trailing or
    /// doubled hyphens (design 7.2). This is exactly the shape <c>SlugGenerator</c> produces.
    /// </summary>
    public const string Slug = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    /// <summary>
    /// A host name such as <c>example.com</c> or <c>mail.example.co.uk</c>: dot-separated labels of letters, digits
    /// and inner hyphens, ending in a letter-only top-level domain. Used for domain blocks (design 8.3).
    /// </summary>
    public const string Domain = "^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$";

    /// <summary>A lowercase hex SHA-256 digest, such as a commenter's IP hash.</summary>
    public const string Sha256Hex = "^[0-9a-f]{64}$";
}
