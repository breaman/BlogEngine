using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Validation;

namespace BlogEngine.UnitTests.Validation;

/// <summary>
/// Tests the shared comment validators (T3.1, T3.7, T3.9): <see cref="CommentSubmissionValidator"/> (name, email,
/// website, body), <see cref="CommentBlockRequestValidator"/>, <see cref="CommentBulkRequestValidator"/> and
/// <see cref="CommentReplyRequestValidator"/>.
/// </summary>
public class CommentValidatorsTests
{
    private static readonly CommentSubmissionValidator SubmissionValidator = new();
    private static readonly CommentBlockRequestValidator BlockValidator = new();
    private static readonly CommentBulkRequestValidator BulkValidator = new();
    private static readonly CommentReplyRequestValidator ReplyValidator = new();

    private static CommentSubmission Valid() => new()
    {
        AuthorName = "Ada",
        AuthorEmail = "ada@example.com",
        AuthorUrl = "https://ada.example",
        Body = "Great post!"
    };

    /// <summary>A complete comment passes, and so does one without a website.</summary>
    [Test]
    public async Task Submission_Valid_Passes()
    {
        var withoutWebsite = Valid();
        withoutWebsite.AuthorUrl = null;

        await Assert.That(SubmissionValidator.Validate(Valid()).IsValid).IsTrue();
        await Assert.That(SubmissionValidator.Validate(withoutWebsite).IsValid).IsTrue();
    }

    /// <summary>Name, email and body are required; blank (whitespace) counts as missing.</summary>
    [Test]
    public async Task Submission_RequiredFields()
    {
        var result = SubmissionValidator.Validate(new CommentSubmission { AuthorName = "  ", AuthorEmail = "", Body = "\n" });

        await Assert.That(result.ToDictionary().Keys).IsEquivalentTo(
            [nameof(CommentSubmission.AuthorName), nameof(CommentSubmission.AuthorEmail), nameof(CommentSubmission.Body)]);
    }

    /// <summary>The email must look like an email address.</summary>
    [Test]
    public async Task Submission_InvalidEmail_Fails()
    {
        var comment = Valid();
        comment.AuthorEmail = "not-an-email";

        var result = SubmissionValidator.Validate(comment);

        await Assert.That(result.ToDictionary().Keys).IsEquivalentTo([nameof(CommentSubmission.AuthorEmail)]);
        await Assert.That(result.Errors.Single().ErrorMessage).Contains("'Email'");
    }

    /// <summary>The website must be an absolute http(s) URL, so it can't smuggle in <c>javascript:</c>.</summary>
    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("ftp://example.com")]
    [Arguments("example.com")]
    [Arguments("/relative")]
    public async Task Submission_InvalidWebsite_Fails(string url)
    {
        var comment = Valid();
        comment.AuthorUrl = url;

        var result = SubmissionValidator.Validate(comment);

        await Assert.That(result.ToDictionary().Keys).IsEquivalentTo([nameof(CommentSubmission.AuthorUrl)]);
        await Assert.That(result.Errors.Single().ErrorMessage).Contains("'Website'");
    }

    /// <summary>Lengths come from <see cref="FieldLengths"/>.</summary>
    [Test]
    public async Task Submission_EnforcesLengths()
    {
        var atLimit = new CommentSubmission
        {
            AuthorName = new string('n', FieldLengths.PersonName),
            AuthorEmail = new string('e', FieldLengths.Email - "@example.com".Length) + "@example.com",
            AuthorUrl = "https://example.com/" + new string('u', FieldLengths.Url - "https://example.com/".Length),
            Body = new string('b', FieldLengths.CommentBody)
        };
        var tooLong = new CommentSubmission
        {
            AuthorName = atLimit.AuthorName + "n",
            AuthorEmail = "e" + atLimit.AuthorEmail,
            AuthorUrl = atLimit.AuthorUrl + "u",
            Body = atLimit.Body + "b"
        };

        await Assert.That(SubmissionValidator.Validate(atLimit).IsValid).IsTrue();
        await Assert.That(SubmissionValidator.Validate(tooLong).ToDictionary().Keys).IsEquivalentTo(
        [
            nameof(CommentSubmission.AuthorName), nameof(CommentSubmission.AuthorEmail),
            nameof(CommentSubmission.AuthorUrl), nameof(CommentSubmission.Body)
        ]);
    }

    /// <summary>Block values are checked for their kind, in normalized form.</summary>
    [Test]
    [Arguments(CommentBlockKind.Keyword, "casino", true)]
    [Arguments(CommentBlockKind.Keyword, " x ", false)]
    [Arguments(CommentBlockKind.Domain, "spam.example", true)]
    [Arguments(CommentBlockKind.Domain, "@Spam.Example.", true)]
    [Arguments(CommentBlockKind.Domain, "not a domain", false)]
    [Arguments(CommentBlockKind.Domain, "https://spam.example/", false)]
    [Arguments(CommentBlockKind.Email, "bot@spam.example", true)]
    [Arguments(CommentBlockKind.Email, "bot", false)]
    [Arguments(CommentBlockKind.IpHash, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [Arguments(CommentBlockKind.IpHash, "10.0.0.1", false)]
    public async Task Block_ValueMatchesKind(CommentBlockKind kind, string value, bool valid)
    {
        await Assert.That(BlockValidator.Validate(new CommentBlockRequest { Kind = kind, Value = value }).IsValid).IsEqualTo(valid);
    }

    /// <summary>A block needs a value and a known kind; the note has a maximum length.</summary>
    [Test]
    public async Task Block_RequiresValueAndKnownKind()
    {
        var result = BlockValidator.Validate(new CommentBlockRequest
        {
            Kind = (CommentBlockKind)42,
            Value = "",
            Note = new string('n', FieldLengths.CommentBlockNote + 1)
        });

        await Assert.That(result.ToDictionary().Keys).IsEquivalentTo(
            [nameof(CommentBlockRequest.Kind), nameof(CommentBlockRequest.Value), nameof(CommentBlockRequest.Note)]);
    }

    /// <summary>A bulk request needs between one and <see cref="CommentBulkRequest.MaxIds"/> comments and a known action.</summary>
    [Test]
    public async Task Bulk_LimitsIdsAndAction()
    {
        var ok = BulkValidator.Validate(new CommentBulkRequest { Ids = [1, 2], Action = CommentModerationAction.Spam });
        var empty = BulkValidator.Validate(new CommentBulkRequest { Ids = [], Action = CommentModerationAction.Approve });
        var tooMany = BulkValidator.Validate(new CommentBulkRequest { Ids = [.. Enumerable.Range(1, CommentBulkRequest.MaxIds + 1)] });
        var badAction = BulkValidator.Validate(new CommentBulkRequest { Ids = [1], Action = (CommentModerationAction)9 });

        await Assert.That(ok.IsValid).IsTrue();
        await Assert.That(empty.IsValid).IsFalse();
        await Assert.That(tooMany.IsValid).IsFalse();
        await Assert.That(badAction.IsValid).IsFalse();
    }

    /// <summary>The author's reply needs a body within the comment length (T4.15).</summary>
    [Test]
    public async Task Reply_RequiresBodyWithinLength()
    {
        var valid = ReplyValidator.Validate(new CommentReplyRequest { Body = "Thanks for reading!" });
        var empty = ReplyValidator.Validate(new CommentReplyRequest { Body = "  " });
        var tooLong = ReplyValidator.Validate(new CommentReplyRequest { Body = new string('a', FieldLengths.CommentBody + 1) });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(empty.Errors.Single().ErrorMessage).IsEqualTo("'Reply' must not be empty.");
        await Assert.That(tooLong.IsValid).IsFalse();
    }
}
