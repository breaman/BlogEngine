using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="CommentBlockValues"/>: blocklist values are stored in one canonical form (T3.9).
/// </summary>
public class CommentBlockValuesTests
{
    /// <summary>Values are trimmed and lowercased; keywords collapse whitespace; domains lose a leading @ and trailing dot.</summary>
    [Test]
    [Arguments(CommentBlockKind.Keyword, "  Cheap \t  PILLS ", "cheap pills")]
    [Arguments(CommentBlockKind.Domain, " @Spam.Example. ", "spam.example")]
    [Arguments(CommentBlockKind.Email, " Bot@Spam.Example ", "bot@spam.example")]
    [Arguments(CommentBlockKind.IpHash, " ABCDEF ", "abcdef")]
    [Arguments(CommentBlockKind.Keyword, null, "")]
    public async Task Normalize(CommentBlockKind kind, string? value, string expected)
    {
        await Assert.That(CommentBlockValues.Normalize(kind, value)).IsEqualTo(expected);
    }
}