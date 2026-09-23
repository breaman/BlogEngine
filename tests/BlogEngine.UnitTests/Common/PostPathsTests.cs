using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="PostPaths"/>: the canonical post URL form (design 7.1, 16).
/// </summary>
public class PostPathsTests
{
    /// <summary>Month and day are always zero-padded.</summary>
    [Test]
    [Arguments(2026, 9, 2, "/posts/2026/09/02/hello")]
    [Arguments(2026, 12, 31, "/posts/2026/12/31/hello")]
    public async Task Post_ZeroPadsDate(int year, int month, int day, string expected)
    {
        await Assert.That(PostPaths.Post(new DateOnly(year, month, day), "hello")).IsEqualTo(expected);
    }
}
