using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers for comment tests: posting the public comment form the way a browser without JavaScript does, seeding
/// comments directly, and reading them back.
/// </summary>
public static class CommentTestData
{
    private static int lastAddress;

    /// <summary>A client address no other test uses, so each test gets its own rate-limit window.</summary>
    public static string NextIp()
    {
        var n = Interlocked.Increment(ref lastAddress);
        return $"198.18.{n / 250}.{(n % 250) + 1}";
    }

    /// <summary>A cookie-keeping, non-redirecting client that appears to come from <paramref name="ip"/> (a fresh address by default).</summary>
    public static HttpClient CreateClient(BlogEngineWebApplicationFactory factory, string? ip = null)
    {
        var client = IdentityTestHelper.CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, ip ?? NextIp());
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CommentTests/1.0");
        return client;
    }

    /// <summary>Publishes a post that takes comments and returns it.</summary>
    public static Task<PostEditDto> PublishPostAsync(BlogEngineWebApplicationFactory factory, string title, bool allowComments = true)
    {
        return PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = title, ContentMarkdown = "Body.", AllowComments = allowComments },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 5, 1));
    }

    /// <summary>
    /// Posts the comment form on the post page with its antiforgery token, a render timestamp <paramref name="formAge"/>
    /// old (a minute by default, past the time trap), and an empty honeypot unless one is given; with
    /// <paramref name="parentId"/>, as a reply to that comment. The token comes from <paramref name="tokenPage"/> when the
    /// post itself shows no form (comments closed).
    /// </summary>
    public static async Task<HttpResponseMessage> SubmitAsync(BlogEngineWebApplicationFactory factory, HttpClient client, PostEditDto post,
        string body, string name = "Ada Reader", string? email = null, string? website = null, string? honeypot = null,
        TimeSpan? formAge = null, int? parentId = null, string? tokenPage = null)
    {
        var timestamps = factory.Services.GetRequiredService<CommentFormTimestamp>();
        var fields = new Dictionary<string, string>
        {
            ["Input.AuthorName"] = name,
            ["Input.AuthorEmail"] = email ?? $"reader-{PublicTestPosts.Token()}@example.com",
            ["Input.AuthorUrl"] = website ?? string.Empty,
            ["Input.Body"] = body,
            [CommentForm.HoneypotField] = honeypot ?? string.Empty,
            [CommentForm.TimestampField] = timestamps.Create(post.Id, DateTimeOffset.UtcNow - (formAge ?? TimeSpan.FromMinutes(1)))
        };
        if (parentId is { } parent)
        {
            fields["Input.ParentCommentId"] = parent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return await IdentityTestHelper.SubmitFormAsync(client, post.PublicPath!, CommentForm.FormName, fields, tokenPage);
    }

    /// <summary>The body of <paramref name="response"/>, HTML-decoded.</summary>
    public static async Task<string> ReadHtmlAsync(HttpResponseMessage response)
    {
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Adds a comment straight to the database, optionally backdated.</summary>
    public static async Task<Comment> AddAsync(BlogEngineWebApplicationFactory factory, int postId, CommentStatus status, string body,
        string? email = null, string? ipHash = null, string name = "Seeded Reader", string? website = null, DateTimeOffset? createdOn = null,
        int? parentId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var comment = new Comment
        {
            PostId = postId,
            ParentCommentId = parentId,
            AuthorName = name,
            AuthorEmail = email ?? $"seed-{PublicTestPosts.Token()}@example.com",
            AuthorUrl = website,
            BodyMarkdown = body,
            BodyHtml = $"<p>{WebUtility.HtmlEncode(body)}</p>",
            Status = status,
            IpHash = ipHash ?? Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(64, '0')
        };
        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync();

        if (createdOn is { } date)
        {
            // The fingerprint interceptor stamps CreatedOn with the current time; backdate it afterwards.
            await dbContext.Comments.Where(c => c.Id == comment.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedOn, date));
            comment.CreatedOn = date;
        }

        return comment;
    }

    /// <summary>The comments on a post, oldest first.</summary>
    public static async Task<List<Comment>> ForPostAsync(BlogEngineWebApplicationFactory factory, int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Comments.AsNoTracking().Where(c => c.PostId == postId).OrderBy(c => c.Id).ToListAsync();
    }

    /// <summary>One comment, or <see langword="null"/> if it was deleted.</summary>
    public static async Task<Comment?> FindAsync(BlogEngineWebApplicationFactory factory, int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Comments.AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Id == id);
    }
}
