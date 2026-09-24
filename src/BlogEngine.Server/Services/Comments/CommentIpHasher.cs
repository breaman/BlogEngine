using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Turns a commenter's IP address into the salted hash stored in <c>Comment.IpHash</c> (design 6.5, 12.3), so the raw
/// address is never stored yet the same address can still be blocked and rate limited.
/// </summary>
/// <remarks>
/// The hash is an HMAC-SHA256 keyed with the secret salt from <see cref="CommentOptions.IpHashSalt"/>: without the
/// salt, the small IPv4 space could be hashed exhaustively and the addresses recovered. IPv4 addresses that arrive as
/// IPv4-mapped IPv6 are hashed as IPv4, so one reader always gets one hash.
/// </remarks>
public sealed class CommentIpHasher
{
    /// <summary>What is hashed when the connection has no remote address (in-process tests, some proxies).</summary>
    public const string UnknownAddress = "unknown";

    private readonly byte[] key;

    /// <summary>Reads the salt, or generates a temporary one with a warning when none is configured.</summary>
    public CommentIpHasher(IOptions<CommentOptions> options, ILogger<CommentIpHasher> logger)
    {
        var salt = options.Value.IpHashSalt;
        if (string.IsNullOrWhiteSpace(salt))
        {
            logger.LogWarning("{Setting} is not configured, so a random salt is used for commenter IP hashes. " +
                "IP blocks and rate limits will not survive a restart.", $"{CommentOptions.SectionName}:{nameof(CommentOptions.IpHashSalt)}");
            key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            key = Encoding.UTF8.GetBytes(salt);
        }
    }

    /// <summary>The lowercase hex hash of <paramref name="address"/> (64 characters).</summary>
    public string Hash(IPAddress? address)
    {
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        var text = address?.ToString() ?? UnknownAddress;
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)));
    }
}
