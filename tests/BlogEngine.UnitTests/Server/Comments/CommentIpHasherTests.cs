using System.Net;

using BlogEngine.Server.Services.Comments;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BlogEngine.UnitTests.Server.Comments;

/// <summary>
/// Tests <see cref="CommentIpHasher"/> (design 6.5, 12.3): salted, stable, and never the raw address.
/// </summary>
public class CommentIpHasherTests
{
    private static CommentIpHasher Create(string? salt) =>
        new(Options.Create(new CommentOptions { IpHashSalt = salt }), NullLogger<CommentIpHasher>.Instance);

    /// <summary>The same address and salt always give the same 64-character lowercase hex hash.</summary>
    [Test]
    public async Task Hash_IsStableHex()
    {
        var hasher = Create("pepper");

        var first = hasher.Hash(IPAddress.Parse("203.0.113.7"));
        var second = hasher.Hash(IPAddress.Parse("203.0.113.7"));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).Matches("^[0-9a-f]{64}$");
        await Assert.That(first).DoesNotContain("203");
    }

    /// <summary>The salt changes the hash, so hashes can't be precomputed without it.</summary>
    [Test]
    public async Task Hash_DependsOnSalt()
    {
        var address = IPAddress.Parse("203.0.113.7");

        await Assert.That(Create("one").Hash(address)).IsNotEqualTo(Create("two").Hash(address));
    }

    /// <summary>An IPv4 address seen as IPv4-mapped IPv6 hashes like plain IPv4.</summary>
    [Test]
    public async Task Hash_MapsIpv4InIpv6()
    {
        var hasher = Create("pepper");

        await Assert.That(hasher.Hash(IPAddress.Parse("::ffff:203.0.113.7"))).IsEqualTo(hasher.Hash(IPAddress.Parse("203.0.113.7")));
    }

    /// <summary>A missing address still gets a hash; without a configured salt a random one is used.</summary>
    [Test]
    public async Task Hash_WorksWithoutAddressOrSalt()
    {
        var hash = Create(null).Hash(null);

        await Assert.That(hash).Matches("^[0-9a-f]{64}$");
    }
}
