namespace BlogEngine.Server.Services;

/// <summary>
/// Optional admin account to create on startup (design 12.1), bound from the <see cref="SectionName"/>
/// configuration section. The Aspire AppHost fills it from its <c>admin-email</c> and
/// <c>admin-password</c> parameters when they are configured.
/// </summary>
public sealed class AdminSeedOptions
{
    /// <summary>Configuration section holding these options.</summary>
    public const string SectionName = "AdminSeed";

    /// <summary>Admin email (also the user name). Seeding is skipped when empty.</summary>
    public string? Email { get; set; }

    /// <summary>Admin password. Seeding is skipped when empty.</summary>
    public string? Password { get; set; }

    /// <summary>Display name for the admin; defaults to "Admin".</summary>
    public string? DisplayName { get; set; }
}