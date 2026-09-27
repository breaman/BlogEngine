using BlogEngine.Data.Models;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Evicts the public caches when a scheduled post goes live (design 6.3, 11, A9, T4.1).
/// </summary>
/// <remarks>
/// <para>
/// Scheduling itself needs no job: the visibility rule (<c>VisibleToPublic</c>) hides a post until its publish time
/// passes. But cached lists, pages, feeds and the sitemap were built while the post was hidden, so once a minute this
/// service looks for posts whose publish time fell since its last check and evicts the caches through
/// <see cref="CacheInvalidator"/>, the same way a manual publish does. The public cache entries' expiration is only
/// the fallback if a check fails.
/// </para>
/// <para>
/// It is registered as a singleton and as the hosted service, so tests can call <see cref="CheckAsync"/> directly with
/// a fake <see cref="TimeProvider"/>. The first window starts when the service is created: posts that went live while
/// the server was down need no eviction, because the caches start empty.
/// </para>
/// </remarks>
public sealed class ScheduledPublishWatcher(
    IServiceScopeFactory scopeFactory,
    CacheInvalidator cacheInvalidator,
    TimeProvider timeProvider,
    ILogger<ScheduledPublishWatcher> logger) : BackgroundService
{
    /// <summary>How often the watcher looks for posts that went live (design 11).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _checkLock = new(1, 1);

    /// <summary>The end of the last window checked; posts that went live after it haven't been handled yet.</summary>
    private DateTimeOffset _checkedUntil = timeProvider.GetUtcNow();

    /// <summary>
    /// Finds the published posts whose publish time is after the previous check and not after now, and evicts the
    /// public caches for each of them.
    /// </summary>
    /// <param name="cancellationToken">Cancels the database query; eviction always completes once it starts.</param>
    /// <returns>The ids of the posts that went live in this window, in publish order.</returns>
    public async Task<IReadOnlyList<int>> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            var from = _checkedUntil;
            var until = timeProvider.GetUtcNow();
            if (until <= from)
            {
                return [];
            }

            List<int> wentLive;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                wentLive = await dbContext.Posts
                    .AsNoTracking()
                    .Where(p => p.Status == PostStatus.Published && p.PublishedOn > from && p.PublishedOn <= until)
                    .OrderBy(p => p.PublishedOn)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);
            }

            foreach (var postId in wentLive)
            {
                await cacheInvalidator.PostChangedAsync(postId);
                logger.LogInformation("Scheduled post {PostId} went live; evicted the public caches.", postId);
            }

            // Only move the window once the evictions are done, so a failed check is retried next time.
            _checkedUntil = until;
            return wentLive;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    /// <summary>Runs <see cref="CheckAsync"/> every <see cref="CheckInterval"/> until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A database hiccup must not stop the watcher; the next tick covers the same window again.
                    logger.LogError(ex, "Checking for scheduled posts that went live failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is shutting down.
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _checkLock.Dispose();
        base.Dispose();
    }
}