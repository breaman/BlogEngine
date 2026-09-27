using System.Globalization;
using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// The signed render time in the comment form, behind the spam guard's time trap (design 8.3): bots that post a form
/// within seconds of loading it, or replay one captured long ago, are discarded.
/// </summary>
/// <remarks>
/// The token is protected with ASP.NET Core Data Protection, so a bot can neither forge an old render time nor move
/// a token to another post (the post id is part of the protected payload).
/// </remarks>
public sealed class CommentFormTimestamp(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    private readonly IDataProtector protector = dataProtection.CreateProtector("BlogEngine.Comments.FormTimestamp.v1");

    /// <summary>A token recording that the form for <paramref name="postId"/> was rendered now.</summary>
    public string Create(int postId)
    {
        return Create(postId, timeProvider.GetUtcNow());
    }

    /// <summary>A token recording that the form for <paramref name="postId"/> was rendered at <paramref name="renderedOn"/>.</summary>
    public string Create(int postId, DateTimeOffset renderedOn)
    {
        return protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{postId}:{renderedOn.UtcTicks}"));
    }

    /// <summary>
    /// The render time in <paramref name="token"/>, or <see langword="null"/> when the token is missing, tampered with,
    /// or belongs to another post.
    /// </summary>
    public DateTimeOffset? Read(int postId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tokenPostId)
            && tokenPostId == postId
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks >= 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;
    }
}