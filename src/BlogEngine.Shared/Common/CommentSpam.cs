namespace BlogEngine.Shared.Common;

/// <summary>
/// Spam scoring constants shared by the server's spam guard and the moderation queue (design 8.3).
/// </summary>
public static class CommentSpam
{
    /// <summary>A comment scoring this much or more is spam.</summary>
    public const int Threshold = 50;
}
