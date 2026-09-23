namespace BlogEngine.Shared.Validation;

/// <summary>
/// Regular expressions shared by validation attributes on the client and server.
/// </summary>
public static class ValidationPatterns
{
    /// <summary>
    /// A slug: lowercase ASCII letters and digits in hyphen-separated words, with no leading, trailing or
    /// doubled hyphens (design 7.2). This is exactly the shape <c>SlugGenerator</c> produces.
    /// </summary>
    public const string Slug = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
}
