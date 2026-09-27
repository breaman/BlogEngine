using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests site search at <c>/search?q=</c> (design 15, P9, T4.9): every word must match the title, summary or content of
/// a visible post, title matches come first, it works as a plain GET without JavaScript, and it is rate limited.
/// </summary>
/// <remarks>
/// Each test searches for words of its own and appears from its own client address, so the rate limit partitions of
/// parallel tests don't interfere.
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class SearchTests(BlogEngineWebApplicationFactory factory)
{
    private static int lastAddress;

    /// <summary>Visible posts matching in the content are found; drafts and scheduled posts never are.</summary>
    [Test]
    public async Task FindsVisiblePosts_ByContent()
    {
        var word = $"zeta{PublicTestPosts.Token()}";
        var visible = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = "Visible", ContentMarkdown = $"Deep inside: {word}." });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {word}", ContentMarkdown = word });
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Scheduled {word}" }, DateTimeOffset.UtcNow.AddDays(2));
        using var client = CreateClient();

        var html = await PublicTestPosts.GetOkAsync(client, $"/search?q={word}");

        await Assert.That(html).Contains("1 post matched.");
        await Assert.That(html).Contains($"href=\"{visible.PublicPath}\"");
        await Assert.That(html).DoesNotContain($"Draft {word}");
        await Assert.That(html).DoesNotContain($"Scheduled {word}");
    }

    /// <summary>Every word has to match, in any field and order, ignoring case; posts with all words in the title come first.</summary>
    [Test]
    public async Task AllWordsMustMatch_TitleMatchesFirst()
    {
        var (first, second) = ($"alpha{PublicTestPosts.Token()}", $"beta{PublicTestPosts.Token()}");
        var titleMatch = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"{first} and {second}" },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 1, 1));
        var contentMatch = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = "Body match", Summary = first, ContentMarkdown = second });
        var partial = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Only {first}" });
        using var client = CreateClient();

        var html = await PublicTestPosts.GetOkAsync(client, $"/search?q={second.ToUpperInvariant()}+{first}");

        await Assert.That(html).Contains("2 posts matched.");
        await Assert.That(html).DoesNotContain($"href=\"{partial.PublicPath}\"");
        // The title match is years older, yet ranks above the newer content match.
        await Assert.That(html.IndexOf(titleMatch.PublicPath!, StringComparison.Ordinal))
            .IsLessThan(html.IndexOf(contentMatch.PublicPath!, StringComparison.Ordinal));
    }

    /// <summary>
    /// Without a query the page shows just the form; with one, the form keeps the words, as a plain GET form that works
    /// without JavaScript.
    /// </summary>
    [Test]
    public async Task Form_WorksWithoutJavaScript()
    {
        using var client = CreateClient();

        var empty = await PublicTestPosts.GetOkAsync(client, "/search");
        var searched = await PublicTestPosts.GetOkAsync(client, "/search?q=%20%20nothing%20%20matches%20this%20" + PublicTestPosts.Token());

        await Assert.That(empty).Contains("action=\"/search\" method=\"get\"");
        await Assert.That(empty).Contains("Type a few words");
        await Assert.That(empty).DoesNotContain("matched.");
        await Assert.That(searched).Contains("No posts matched.");
        await Assert.That(searched).Contains("value=\"nothing matches this ");
        await Assert.That(searched).Contains("<link rel=\"canonical\" href=\"http://localhost/search?q=nothing%20matches%20this%20");
    }

    /// <summary>A page past the last one, or a malformed page number, is 404 like the other lists.</summary>
    [Test]
    public async Task PageOutOfRange_Returns404()
    {
        using var client = CreateClient();

        using var outOfRange = await client.GetAsync($"/search?q=x{PublicTestPosts.Token()}&page=2");
        using var malformed = await client.GetAsync("/search?q=x&page=abc");

        await Assert.That(outOfRange.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>More searches than the limit in one window from one address get a 429; another address is unaffected.</summary>
    [Test]
    public async Task TooManySearches_AreRateLimited()
    {
        using var client = CreateClient();
        using var other = CreateClient();

        for (var i = 0; i < SearchRateLimiting.PermitLimit; i++)
        {
            await PublicTestPosts.GetOkAsync(client, $"/search?q=limit{i}");
        }

        using var rejected = await client.GetAsync("/search?q=one-more");
        var body = await rejected.Content.ReadAsStringAsync();
        using var fromElsewhere = await other.GetAsync("/search?q=one-more");

        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(rejected.Headers.RetryAfter).IsNotNull();
        await Assert.That(body).Contains("Too many searches");
        await Assert.That(fromElsewhere.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>A client appearing from an address no other test uses.</summary>
    private HttpClient CreateClient()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        var n = Interlocked.Increment(ref lastAddress);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, $"10.40.{n / 250}.{n % 250 + 1}");
        return client;
    }
}