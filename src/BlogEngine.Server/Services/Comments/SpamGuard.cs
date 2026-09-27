using System.Text.RegularExpressions;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Decides what happens to a submitted comment (design 8.3, C4, Q2): the layered spam checks, in order.
/// </summary>
/// <remarks>
/// <para>
/// The checks, in the order the design lists them:
/// </para>
/// <list type="number">
/// <item>Honeypot field filled in → <see cref="SpamOutcome.Discard"/>.</item>
/// <item>Time trap: submitted under <see cref="MinimumFormTime"/> after the form rendered, over
/// <see cref="MaximumFormAge"/> after, or with a missing or forged timestamp → <see cref="SpamOutcome.Discard"/>.</item>
/// <item>(Rate limiting happens before this class, in the ASP.NET Core rate limiter.)</item>
/// <item>Email, IP hash, or a domain of the email, website or a link on the blocklist → <see cref="SpamOutcome.Spam"/>.</item>
/// <item>Blocked keyword in the name or body → +<see cref="KeywordScore"/>.</item>
/// <item>More links than the "max links" setting → +<see cref="ExtraLinkScore"/> for each extra one.</item>
/// <item>Body shorter than <see cref="MinimumBodyLength"/> characters, or nothing but a URL → +<see cref="ShortOrLinkOnlyScore"/>.</item>
/// </list>
/// <para>
/// A score of <see cref="SpamThreshold"/> or more is spam; anything else waits for moderation, unless the settings
/// approve it straight away (approval not required, or a returning commenter with auto-approve on), which needs a
/// clean score of 0. Discarded comments are never stored, and the commenter sees the same "thanks" as everyone else.
/// </para>
/// <para>Pure logic with no dependencies, so every check is unit-tested directly.</para>
/// </remarks>
public sealed partial class SpamGuard
{
    /// <summary>Score at which a comment is spam.</summary>
    public const int SpamThreshold = CommentSpam.Threshold;

    /// <summary>Score added for a blocked keyword.</summary>
    public const int KeywordScore = 50;

    /// <summary>Score added for each link beyond the allowed number.</summary>
    public const int ExtraLinkScore = 30;

    /// <summary>Score added for a very short or link-only body.</summary>
    public const int ShortOrLinkOnlyScore = 40;

    /// <summary>Score recorded for a blocklist match.</summary>
    public const int BlockedScore = 100;

    /// <summary>Bodies shorter than this (after trimming) are suspicious.</summary>
    public const int MinimumBodyLength = 3;

    /// <summary>People take at least this long to fill in the form.</summary>
    public static readonly TimeSpan MinimumFormTime = TimeSpan.FromSeconds(3);

    /// <summary>Forms older than this are treated as replayed.</summary>
    public static readonly TimeSpan MaximumFormAge = TimeSpan.FromHours(24);

    /// <summary>Evaluates <paramref name="check"/> against every rule.</summary>
    public SpamVerdict Evaluate(SpamCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!string.IsNullOrEmpty(check.Honeypot))
        {
            return SpamVerdict.Discarded("Honeypot field was filled in");
        }

        if (check.FormRenderedOn is not { } renderedOn)
        {
            return SpamVerdict.Discarded("Form timestamp was missing or invalid");
        }

        var formTime = check.Now - renderedOn;
        if (formTime < MinimumFormTime)
        {
            return SpamVerdict.Discarded("Submitted too quickly after the form loaded");
        }

        if (formTime > MaximumFormAge)
        {
            return SpamVerdict.Discarded("Form was more than 24 hours old");
        }

        var comment = check.Submission;
        var body = comment.Body ?? string.Empty;
        var links = LinkPattern().Matches(body).Select(m => m.Value).ToList();
        var reasons = new List<string>();
        var score = 0;
        var blocked = false;

        foreach (var reason in BlocklistMatches(check, links))
        {
            reasons.Add(reason);
            blocked = true;
        }

        if (blocked)
        {
            score += BlockedScore;
        }

        var keywords = check.Blocklist.Keywords
            .Where(k => body.Contains(k, StringComparison.OrdinalIgnoreCase)
                || (comment.AuthorName ?? string.Empty).Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (keywords.Count > 0)
        {
            score += KeywordScore;
            reasons.Add($"Blocked keyword: {string.Join(", ", keywords)}");
        }

        var extraLinks = links.Count - Math.Max(0, check.MaxLinks);
        if (extraLinks > 0)
        {
            score += extraLinks * ExtraLinkScore;
            reasons.Add($"{links.Count} links (more than {Math.Max(0, check.MaxLinks)})");
        }

        var trimmed = body.Trim();
        if (trimmed.Length < MinimumBodyLength)
        {
            score += ShortOrLinkOnlyScore;
            reasons.Add("Very short comment");
        }
        else if (links.Count > 0 && LinkPattern().Replace(trimmed, string.Empty).Trim(' ', '<', '>', '\r', '\n', '\t').Length == 0)
        {
            score += ShortOrLinkOnlyScore;
            reasons.Add("Comment is only a link");
        }

        var status = (blocked || score >= SpamThreshold) switch
        {
            true => CommentStatus.Spam,
            false when score == 0 && (!check.RequireApproval || (check.AutoApproveReturning && check.HasApprovedComment)) => CommentStatus.Approved,
            false => CommentStatus.Pending
        };

        return new SpamVerdict(SpamOutcome.Keep, status, score, reasons);
    }

    /// <summary>The blocklist entries the comment matches, as moderator-readable reasons.</summary>
    private static IEnumerable<string> BlocklistMatches(SpamCheck check, IReadOnlyList<string> links)
    {
        var blocklist = check.Blocklist;
        var email = (check.Submission.AuthorEmail ?? string.Empty).Trim();

        if (blocklist.Emails.Contains(email))
        {
            yield return "Blocked email";
        }

        if (blocklist.IpHashes.Contains(check.IpHash))
        {
            yield return "Blocked IP address";
        }

        if (blocklist.Domains.Count == 0)
        {
            yield break;
        }

        var hosts = new List<string>();
        if (email.LastIndexOf('@') is var at and >= 0)
        {
            hosts.Add(email[(at + 1)..]);
        }

        hosts.AddRange(new[] { check.Submission.AuthorUrl }.Concat(links).Select(HostOf).OfType<string>());

        var matched = blocklist.Domains
            .Where(domain => hosts.Any(host => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matched.Count > 0)
        {
            yield return $"Blocked domain: {string.Join(", ", matched)}";
        }
    }

    /// <summary>The host of an absolute or <c>www.</c> URL, or <see langword="null"/>.</summary>
    private static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var candidate = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>
    /// A link in the body: an http(s) URL or a bare <c>www.</c> address, as bare text, in angle brackets, or as the
    /// target of a Markdown link.
    /// </summary>
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>()\[\]""']+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}

/// <summary>Everything <see cref="SpamGuard"/> looks at for one submission.</summary>
/// <param name="Submission">The comment as submitted.</param>
/// <param name="Honeypot">Value of the hidden honeypot field; people leave it empty.</param>
/// <param name="FormRenderedOn">When the form was rendered, from its signed timestamp; <see langword="null"/> if missing or forged.</param>
/// <param name="Now">The current time.</param>
/// <param name="IpHash">The commenter's salted IP hash.</param>
/// <param name="Blocklist">The blocklist.</param>
/// <param name="MaxLinks">Links allowed before the score increases (setting).</param>
/// <param name="RequireApproval">Whether comments wait for moderation (setting; on at launch).</param>
/// <param name="AutoApproveReturning">Whether returning commenters skip moderation (setting; off by default).</param>
/// <param name="HasApprovedComment">Whether this email already has an approved comment.</param>
public sealed record SpamCheck(
    CommentSubmission Submission,
    string? Honeypot,
    DateTimeOffset? FormRenderedOn,
    DateTimeOffset Now,
    string IpHash,
    CommentBlocklist Blocklist,
    int MaxLinks,
    bool RequireApproval = true,
    bool AutoApproveReturning = false,
    bool HasApprovedComment = false);

/// <summary>The blocklist, split by kind, with values in their normalized (lowercase) form.</summary>
/// <param name="Emails">Blocked email addresses.</param>
/// <param name="IpHashes">Blocked IP hashes.</param>
/// <param name="Domains">Blocked domains; subdomains match too.</param>
/// <param name="Keywords">Blocked words and phrases.</param>
public sealed record CommentBlocklist(
    IReadOnlySet<string> Emails,
    IReadOnlySet<string> IpHashes,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Keywords)
{
    /// <summary>A blocklist with nothing on it.</summary>
    public static CommentBlocklist Empty { get; } = Create([]);

    /// <summary>Builds the blocklist from (kind, value) pairs such as the rows of <c>CommentBlocks</c>.</summary>
    public static CommentBlocklist Create(IEnumerable<(CommentBlockKind Kind, string Value)> entries)
    {
        var list = entries.ToList();
        IEnumerable<string> Values(CommentBlockKind kind) => list.Where(e => e.Kind == kind).Select(e => e.Value);

        return new CommentBlocklist(
            new HashSet<string>(Values(CommentBlockKind.Email), StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(Values(CommentBlockKind.IpHash), StringComparer.OrdinalIgnoreCase),
            [.. Values(CommentBlockKind.Domain)],
            [.. Values(CommentBlockKind.Keyword)]);
    }
}

/// <summary>Whether a submission is stored at all.</summary>
public enum SpamOutcome
{
    /// <summary>Stored with <see cref="SpamVerdict.Status"/>.</summary>
    Keep,

    /// <summary>Thrown away without a trace (honeypot or time trap); the sender still sees "thanks".</summary>
    Discard
}

/// <summary>The spam guard's decision.</summary>
/// <param name="Outcome">Whether to store the comment.</param>
/// <param name="Status">The status to store it with (meaningless for a discard).</param>
/// <param name="Score">The spam score.</param>
/// <param name="Reasons">Why, for the moderator (and the log for discards).</param>
public sealed record SpamVerdict(SpamOutcome Outcome, CommentStatus Status, int Score, IReadOnlyList<string> Reasons)
{
    /// <summary>A discard for <paramref name="reason"/>.</summary>
    public static SpamVerdict Discarded(string reason)
    {
        return new SpamVerdict(SpamOutcome.Discard, CommentStatus.Spam, 0, [reason]);
    }
}