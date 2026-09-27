namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// <c>[NotInParallel]</c> keys for tests that change state shared by the whole session's database.
/// </summary>
public static class TestConstraints
{
    /// <summary>Tests that add or delete user accounts (for example resetting to "no users" for <c>/setup</c>).</summary>
    public const string Users = nameof(Users);

    /// <summary>Tests that change the single site settings row.</summary>
    public const string SiteSettings = nameof(SiteSettings);

    /// <summary>Tests that add or moderate comments, whose counts and moderation queue the whole session shares.</summary>
    public const string Comments = nameof(Comments);
}