using System.Security.Claims;

using BlogEngine.Data.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BlogEngine.Server.Services;

/// <summary>
/// Extends the default claims principal factory to include the user's display and first names as claims
/// so that the UI can display "Welcome, {name}" without an extra database query.
/// </summary>
public class CustomUserClaimsPrincipalFactory(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<User, Role>(userManager, roleManager, optionsAccessor)
{
    /// <summary>Claim type holding <see cref="User.DisplayName"/>.</summary>
    public const string DisplayNameClaimType = "DisplayName";

    /// <summary>Claim type holding <see cref="User.FirstName"/>.</summary>
    public const string FirstNameClaimType = "FirstName";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(DisplayNameClaimType, user.DisplayName));
        }

        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            identity.AddClaim(new Claim(FirstNameClaimType, user.FirstName));
        }

        return identity;
    }
}