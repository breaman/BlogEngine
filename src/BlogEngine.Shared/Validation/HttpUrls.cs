namespace BlogEngine.Shared.Validation;

/// <summary>
/// URL checks shared by the validators.
/// </summary>
internal static class HttpUrls
{
    /// <summary>
    /// Whether <paramref name="url"/> is an absolute http or https URL, the only kind a browser should open from a
    /// link the site renders. Blank values pass, so a required rule reports them once, with its own message.
    /// </summary>
    public static bool IsBlankOrAbsoluteHttp(string? url)
    {
        return string.IsNullOrEmpty(url)
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
    }
}
