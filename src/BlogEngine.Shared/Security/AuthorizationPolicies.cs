using Microsoft.AspNetCore.Authorization;

namespace BlogEngine.Shared.Security;

/// <summary>
/// Authorization policy names and their registration (design 12.1). Every admin page and admin API
/// endpoint uses <see cref="AdminOnly"/>, so tightening admin access later means changing one policy.
/// </summary>
/// <example>
/// <code>
/// // Program.cs (server and client)
/// builder.Services.AddAuthorizationCore(options => options.AddBlogPolicies());
///
/// // A Razor page
/// @attribute [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
/// </code>
/// </example>
public static class AuthorizationPolicies
{
    /// <summary>Requires an authenticated user in the <see cref="AppRoles.Admin"/> role.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>
    /// Registers the blog's policies. Called by both the server and the WebAssembly client, because each
    /// evaluates <c>[Authorize(Policy = ...)]</c> and <c>AuthorizeView</c> against its own options.
    /// </summary>
    public static AuthorizationOptions AddBlogPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AdminOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(AppRoles.Admin));

        return options;
    }
}
