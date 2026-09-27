namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Server-side comment settings that aren't editable in the admin (design 8.3), bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class CommentOptions
{
    /// <summary>Configuration section holding these options.</summary>
    public const string SectionName = "Comments";

    /// <summary>
    /// Secret salt for commenter IP hashes (design 6.5, 12.3). Keep it stable: IP blocks only match hashes made with
    /// the same salt. The Aspire AppHost generates one and keeps it in its user secrets. When it is empty the server
    /// picks a random salt at startup, so IP blocks stop matching after a restart.
    /// </summary>
    public string? IpHashSalt { get; set; }

    /// <summary>Comments one IP address may post per <see cref="RateLimitWindow"/> (design 8.3).</summary>
    public int RateLimitPermits { get; set; } = 3;

    /// <summary>The fixed rate-limit window.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(5);
}