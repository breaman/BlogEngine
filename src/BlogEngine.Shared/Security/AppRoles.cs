namespace BlogEngine.Shared.Security;

/// <summary>
/// Names of the Identity roles the blog uses (design 3). Shared so the server, which assigns roles, and
/// the WebAssembly client, which checks them, can't drift apart.
/// </summary>
public static class AppRoles
{
    /// <summary>The single author/administrator account; grants everything under <c>/admin</c> and <c>/api/admin</c>.</summary>
    public const string Admin = "Admin";
}