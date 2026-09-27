using BlogEngine.Server.Storage;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Tests <see cref="MediaStorageHealthCheck"/> (design 18, T2.1): healthy when a probe can be written and removed,
/// unhealthy when storage refuses writes.
/// </summary>
public class MediaStorageHealthCheckTests
{
    /// <summary>A writable folder is healthy and the probe is cleaned up.</summary>
    [Test]
    public async Task WritableStorage_IsHealthy()
    {
        var root = Path.Combine(Path.GetTempPath(), "blogengine-health-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var check = new MediaStorageHealthCheck(new FileSystemMediaStorage(root));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(Directory.EnumerateFileSystemEntries(root).Any()).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Overlapping checks don't interfere with each other's probe files.</summary>
    [Test]
    public async Task ConcurrentChecks_AreAllHealthy()
    {
        var root = Path.Combine(Path.GetTempPath(), "blogengine-health-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var check = new MediaStorageHealthCheck(new FileSystemMediaStorage(root));

            var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => check.CheckHealthAsync(new HealthCheckContext()))));

            await Assert.That(results.All(r => r.Status == HealthStatus.Healthy)).IsTrue();
            await Assert.That(Directory.EnumerateFileSystemEntries(root).Any()).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Storage that throws on write is reported unhealthy, not as a crash.</summary>
    [Test]
    [Arguments(typeof(UnauthorizedAccessException))]
    [Arguments(typeof(IOException))]
    public async Task FailingStorage_IsUnhealthy(Type exceptionType)
    {
        var check = new MediaStorageHealthCheck(new FailingStorage((Exception)Activator.CreateInstance(exceptionType)!));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception!.GetType()).IsEqualTo(exceptionType);
    }

    private sealed class FailingStorage(Exception error) : IMediaStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct) => Task.FromException(error);

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;

        public Task DeletePrefixAsync(string prefix, CancellationToken ct) => Task.CompletedTask;
    }
}