using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="RelativeTime"/>, the "3 days ago" dates on comments (T3.6, T3.8).
/// </summary>
public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Each range rounds down to its unit, with singular and plural forms.</summary>
    [Test]
    [Arguments(0, "just now")]
    [Arguments(59, "just now")]
    [Arguments(60, "1 minute ago")]
    [Arguments(5 * 60, "5 minutes ago")]
    [Arguments(60 * 60, "1 hour ago")]
    [Arguments(23 * 60 * 60 + 3599, "23 hours ago")]
    [Arguments(24 * 60 * 60, "1 day ago")]
    [Arguments(3 * 24 * 60 * 60, "3 days ago")]
    [Arguments(45 * 24 * 60 * 60, "1 month ago")]
    [Arguments(200 * 24 * 60 * 60, "6 months ago")]
    [Arguments(400 * 24 * 60 * 60, "1 year ago")]
    [Arguments(800 * 24 * 60 * 60, "2 years ago")]
    public async Task Format(int secondsAgo, string expected)
    {
        await Assert.That(RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now)).IsEqualTo(expected);
    }

    /// <summary>A timestamp slightly in the future (clock skew) reads as "just now".</summary>
    [Test]
    public async Task Future_IsJustNow()
    {
        await Assert.That(RelativeTime.Format(Now.AddMinutes(2), Now)).IsEqualTo("just now");
    }
}
