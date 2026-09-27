using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Server.Comments;

/// <summary>
/// Tests <see cref="SpamGuard"/> (design 8.3, T3.4): every check in order, the score threshold and the approval paths.
/// </summary>
public class SpamGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private const string IpHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly SpamGuard Guard = new();

    private static SpamCheck Check(string body = "Thanks, this helped me a lot.", string email = "ada@example.com", string? url = null,
        string name = "Ada", string? honeypot = null, TimeSpan? formAge = null, CommentBlocklist? blocklist = null, int maxLinks = 2,
        bool requireApproval = true, bool autoApprove = false, bool hasApproved = false, bool missingTimestamp = false)
    {
        return new SpamCheck(
            new CommentSubmission { AuthorName = name, AuthorEmail = email, AuthorUrl = url, Body = body },
            honeypot,
            missingTimestamp ? null : Now - (formAge ?? TimeSpan.FromMinutes(2)),
            Now,
            IpHash,
            blocklist ?? CommentBlocklist.Empty,
            maxLinks,
            requireApproval,
            autoApprove,
            hasApproved);
    }

    private static CommentBlocklist Blocks(params (CommentBlockKind Kind, string Value)[] entries) => CommentBlocklist.Create(entries);

    /// <summary>A normal comment is kept as pending with a clean score.</summary>
    [Test]
    public async Task CleanComment_IsPending()
    {
        var verdict = Guard.Evaluate(Check());

        await Assert.That(verdict.Outcome).IsEqualTo(SpamOutcome.Keep);
        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Pending);
        await Assert.That(verdict.Score).IsEqualTo(0);
        await Assert.That(verdict.Reasons).IsEmpty();
    }

    /// <summary>A filled-in honeypot is discarded, before anything else is looked at.</summary>
    [Test]
    public async Task Honeypot_Discards()
    {
        var verdict = Guard.Evaluate(Check(honeypot: "http://spam.example", missingTimestamp: true));

        await Assert.That(verdict.Outcome).IsEqualTo(SpamOutcome.Discard);
        await Assert.That(verdict.Reasons.Single()).Contains("Honeypot");
    }

    /// <summary>The time trap discards forms posted within 3 seconds, after 24 hours, or without a valid timestamp.</summary>
    [Test]
    [Arguments(0, true)]
    [Arguments(2, true)]
    [Arguments(3, false)]
    [Arguments(24 * 60 * 60, false)]
    [Arguments(24 * 60 * 60 + 1, true)]
    [Arguments(-30, true)]
    public async Task TimeTrap(int ageSeconds, bool discarded)
    {
        var verdict = Guard.Evaluate(Check(formAge: TimeSpan.FromSeconds(ageSeconds)));

        await Assert.That(verdict.Outcome == SpamOutcome.Discard).IsEqualTo(discarded);
    }

    /// <summary>A missing or forged timestamp is discarded.</summary>
    [Test]
    public async Task MissingTimestamp_Discards()
    {
        await Assert.That(Guard.Evaluate(Check(missingTimestamp: true)).Outcome).IsEqualTo(SpamOutcome.Discard);
    }

    /// <summary>A blocked email (any casing) or IP hash sends the comment to spam.</summary>
    [Test]
    public async Task BlockedEmailOrIp_IsSpam()
    {
        var byEmail = Guard.Evaluate(Check(email: "Bot@Spam.Example", blocklist: Blocks((CommentBlockKind.Email, "bot@spam.example"))));
        var byIp = Guard.Evaluate(Check(blocklist: Blocks((CommentBlockKind.IpHash, IpHash))));

        await Assert.That(byEmail.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(byEmail.Reasons).Contains("Blocked email");
        await Assert.That(byIp.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(byIp.Reasons).Contains("Blocked IP address");
        await Assert.That(byIp.Score).IsGreaterThanOrEqualTo(SpamGuard.SpamThreshold);
    }

    /// <summary>A blocked domain matches the email, the website or a link in the body, subdomains included.</summary>
    [Test]
    [Arguments("x@spam.example", null, "Hello there, friend.")]
    [Arguments("ada@example.com", "https://www.spam.example/shop", "Hello there, friend.")]
    [Arguments("ada@example.com", null, "Look at [this](https://deals.spam.example/x) please")]
    [Arguments("ada@example.com", null, "Visit www.spam.example today")]
    public async Task BlockedDomain_IsSpam(string email, string? url, string body)
    {
        var verdict = Guard.Evaluate(Check(body: body, email: email, url: url, blocklist: Blocks((CommentBlockKind.Domain, "spam.example"))));

        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(verdict.Reasons.Single()).IsEqualTo("Blocked domain: spam.example");
    }

    /// <summary>A domain block doesn't match a different domain that merely ends with the same letters.</summary>
    [Test]
    public async Task BlockedDomain_DoesNotMatchLookalike()
    {
        var verdict = Guard.Evaluate(Check(email: "ada@notspam.example", blocklist: Blocks((CommentBlockKind.Domain, "spam.example"))));

        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Pending);
    }

    /// <summary>A blocked keyword in the body or the name adds 50, which reaches the threshold.</summary>
    [Test]
    [Arguments("Buy CHEAP PILLS now, friend", "Ada")]
    [Arguments("A perfectly normal comment", "Cheap pills")]
    public async Task Keyword_Adds50(string body, string name)
    {
        var verdict = Guard.Evaluate(Check(body: body, name: name, blocklist: Blocks((CommentBlockKind.Keyword, "cheap pills"))));

        await Assert.That(verdict.Score).IsEqualTo(SpamGuard.KeywordScore);
        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(verdict.Reasons.Single()).IsEqualTo("Blocked keyword: cheap pills");
    }

    /// <summary>Links beyond the allowed number add 30 each: one extra stays pending, two extra reach spam.</summary>
    [Test]
    [Arguments(2, 0, CommentStatus.Pending)]
    [Arguments(3, 30, CommentStatus.Pending)]
    [Arguments(4, 60, CommentStatus.Spam)]
    public async Task ExtraLinks_Add30Each(int linkCount, int score, CommentStatus status)
    {
        var body = "My links: " + string.Join(", ", Enumerable.Range(1, linkCount).Select(i => $"https://site{i}.example/page"));

        var verdict = Guard.Evaluate(Check(body: body));

        await Assert.That(verdict.Score).IsEqualTo(score);
        await Assert.That(verdict.Status).IsEqualTo(status);
    }

    /// <summary>The allowed number of links follows the setting.</summary>
    [Test]
    public async Task MaxLinks_FollowsSetting()
    {
        var verdict = Guard.Evaluate(Check(body: "See https://a.example and <https://b.example>", maxLinks: 0));

        await Assert.That(verdict.Score).IsEqualTo(2 * SpamGuard.ExtraLinkScore);
    }

    /// <summary>A body under 3 characters, or nothing but a link, adds 40 (pending on its own).</summary>
    [Test]
    [Arguments("ok")]
    [Arguments("https://spam.example/buy")]
    [Arguments("<https://spam.example/buy>")]
    [Arguments("  www.spam.example  ")]
    public async Task ShortOrLinkOnly_Adds40(string body)
    {
        var verdict = Guard.Evaluate(Check(body: body));

        await Assert.That(verdict.Score).IsEqualTo(SpamGuard.ShortOrLinkOnlyScore);
        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Pending);
    }

    /// <summary>Scores add up: a link-only comment (+40) with an extra link over a limit of 0 (+30) is spam.</summary>
    [Test]
    public async Task Scores_AddUpToThreshold()
    {
        var verdict = Guard.Evaluate(Check(body: "https://spam.example/buy", maxLinks: 0));

        await Assert.That(verdict.Score).IsEqualTo(70);
        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Spam);
        await Assert.That(verdict.Reasons.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Approval can be skipped only for a clean score: when approval isn't required, or for a returning commenter with
    /// auto-approve on. Auto-approve is off by default (Q2).
    /// </summary>
    [Test]
    [Arguments(true, false, true, CommentStatus.Pending)]
    [Arguments(true, true, false, CommentStatus.Pending)]
    [Arguments(true, true, true, CommentStatus.Approved)]
    [Arguments(false, false, false, CommentStatus.Approved)]
    public async Task ApprovalPaths(bool requireApproval, bool autoApprove, bool hasApproved, CommentStatus expected)
    {
        var verdict = Guard.Evaluate(Check(requireApproval: requireApproval, autoApprove: autoApprove, hasApproved: hasApproved));

        await Assert.That(verdict.Status).IsEqualTo(expected);
    }

    /// <summary>A returning commenter still waits for moderation when the comment scored anything.</summary>
    [Test]
    public async Task AutoApprove_NeedsCleanScore()
    {
        var verdict = Guard.Evaluate(Check(body: "ok", autoApprove: true, hasApproved: true));

        await Assert.That(verdict.Status).IsEqualTo(CommentStatus.Pending);
    }
}