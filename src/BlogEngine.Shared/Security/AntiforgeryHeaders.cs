namespace BlogEngine.Shared.Security;

/// <summary>
/// The HTTP header that carries the antiforgery request token on admin API calls (design 7.4). The
/// server configures antiforgery to read it and the WebAssembly <c>AntiforgeryHandler</c> writes it.
/// </summary>
public static class AntiforgeryHeaders
{
    /// <summary>Header name; ASP.NET Core's default, set explicitly on both sides so they stay in step.</summary>
    public const string RequestToken = "RequestVerificationToken";
}