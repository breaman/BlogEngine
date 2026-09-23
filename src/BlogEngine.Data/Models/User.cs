using BlogEngine.Data.Interfaces;

using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Data.Models;

/// <summary>
/// An account. Column lengths are configured in <see cref="Configurations.UserConfiguration"/>.
/// </summary>
public class User : IdentityUser<int>, IEntityBase
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTimeOffset MemberSince { get; set; }

    /// <summary>
    /// Name shown for the account in the UI (for example the admin created by <c>/setup</c>); falls back to
    /// <see cref="FirstName"/> and then the user name when empty.
    /// </summary>
    public string? DisplayName { get; set; }
}