using BlogEngine.Server.Services.Comments;

using Microsoft.AspNetCore.DataProtection;

namespace BlogEngine.UnitTests.Server.Comments;

/// <summary>
/// Tests <see cref="CommentFormTimestamp"/>, the signed render time behind the time trap (design 8.3, T3.3).
/// </summary>
public class CommentFormTimestampTests
{
    private static readonly CommentFormTimestamp Timestamp = new(new EphemeralDataProtectionProvider(), TimeProvider.System);
    private static readonly DateTimeOffset RenderedOn = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A token reads back as the time it was created for.</summary>
    [Test]
    public async Task RoundTrips()
    {
        var token = Timestamp.Create(7, RenderedOn);

        await Assert.That(Timestamp.Read(7, token)).IsEqualTo(RenderedOn);
    }

    /// <summary>A token for another post, a tampered token or no token reads as nothing.</summary>
    [Test]
    public async Task RejectsOtherPostsAndForgeries()
    {
        var token = Timestamp.Create(7, RenderedOn);

        await Assert.That(Timestamp.Read(8, token)).IsNull();
        await Assert.That(Timestamp.Read(7, token[..^2] + "xx")).IsNull();
        await Assert.That(Timestamp.Read(7, "1695470400")).IsNull();
        await Assert.That(Timestamp.Read(7, null)).IsNull();
    }
}
