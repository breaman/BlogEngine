using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests the comment log events (design 18, T3.11): <c>CommentSubmitted</c> with the spam score, <c>CommentDiscarded</c>
/// for failed spam checks, and <c>CommentModerated</c>.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Comments)]
public class CommentLogTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A stored comment logs CommentSubmitted with its status and score; a honeypot hit logs CommentDiscarded.</summary>
    [Test]
    public async Task Submit_WritesStructuredEvents()
    {
        var logger = new CapturingLogger<CommentSubmissionService>();
        var post = await CommentTestData.PublishPostAsync(factory, $"Logged {PublicTestPosts.Token()}");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<CommentSubmissionService>(scope.ServiceProvider, logger);
        var timestamp = scope.ServiceProvider.GetRequiredService<CommentFormTimestamp>().Create(post.Id, DateTimeOffset.UtcNow.AddMinutes(-1));
        var ipHash = scope.ServiceProvider.GetRequiredService<CommentIpHasher>().Hash(IPAddress.Parse(CommentTestData.NextIp()));
        var submission = new CommentSubmission { AuthorName = "Logged", AuthorEmail = "logged@example.com", Body = "ok" };

        await service.SubmitAsync(post.Id, submission, new CommentSubmissionContext(ipHash, "agent", null, timestamp));
        await service.SubmitAsync(post.Id, submission, new CommentSubmissionContext(ipHash, "agent", "bot", timestamp));

        var submitted = logger.Entries.Single(e => e.EventId.Name == "CommentSubmitted");
        var discarded = logger.Entries.Single(e => e.EventId.Name == "CommentDiscarded");
        await Assert.That(submitted.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(submitted.Properties["PostId"]).IsEqualTo(post.Id);
        await Assert.That(submitted.Properties["Status"]).IsEqualTo(CommentStatus.Pending);
        await Assert.That(submitted.Properties["SpamScore"]).IsEqualTo(40);
        await Assert.That(submitted.Properties["SpamReasons"]).IsEqualTo("Very short comment");
        await Assert.That(discarded.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That((string)discarded.Properties["Reason"]!).Contains("Honeypot");
    }

    /// <summary>Moderating logs CommentModerated with the old and new status.</summary>
    [Test]
    public async Task Moderate_WritesStructuredEvent()
    {
        var logger = new CapturingLogger<ServerCommentModerationService>();
        var post = await CommentTestData.PublishPostAsync(factory, $"Moderated {PublicTestPosts.Token()}");
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, "Moderate me");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<ServerCommentModerationService>(scope.ServiceProvider, logger);

        await service.ModerateAsync(comment.Id, CommentModerationAction.Reject);

        var moderated = logger.Entries.Single(e => e.EventId.Name == "CommentModerated");
        await Assert.That(moderated.Properties["CommentId"]).IsEqualTo(comment.Id);
        await Assert.That(moderated.Properties["PreviousStatus"]).IsEqualTo(CommentStatus.Pending);
        await Assert.That(moderated.Properties["Status"]).IsEqualTo(CommentStatus.Rejected);
    }
}