using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="PageNumber"/>: parsing <c>?page=</c> on public list pages (design 7.1).
/// </summary>
public class PageNumberTests
{
    /// <summary>A missing value is page 1; positive integers are themselves.</summary>
    [Test]
    [Arguments(null, 1)]
    [Arguments("", 1)]
    [Arguments("1", 1)]
    [Arguments("42", 42)]
    public async Task TryParse_Valid_ReturnsPage(string? value, int expected)
    {
        var parsed = PageNumber.TryParse(value, out var page);

        await Assert.That(parsed).IsTrue();
        await Assert.That(page).IsEqualTo(expected);
    }

    /// <summary>Zero, negatives, signs, decimals, text and numbers too large for an int are rejected (404).</summary>
    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("+2")]
    [Arguments("1.5")]
    [Arguments(" 2")]
    [Arguments("abc")]
    [Arguments("99999999999")]
    public async Task TryParse_Invalid_ReturnsFalse(string value)
    {
        await Assert.That(PageNumber.TryParse(value, out _)).IsFalse();
    }
}
