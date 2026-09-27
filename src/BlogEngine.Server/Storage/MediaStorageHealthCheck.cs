using System.Text;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlogEngine.Server.Storage;

/// <summary>
/// Reports whether media storage accepts writes (design 18): writes, reads back and deletes a small probe
/// object through <see cref="IMediaStorage"/>, so it checks whichever implementation is configured.
/// </summary>
/// <remarks>
/// Each check uses its own probe key, so overlapping checks (several monitors, or a slow check and the next one) can't
/// delete each other's probe. Probes sit at the root of the storage, which is never removed as an empty folder.
/// </remarks>
public sealed class MediaStorageHealthCheck(IMediaStorage storage) : IHealthCheck
{
    /// <summary>Name of the check in the <c>/health</c> report.</summary>
    public const string Name = "media-storage";

    /// <summary>Start of every probe key; the leading dot keeps probes apart from media public ids.</summary>
    public const string ProbeKeyPrefix = ".health-probe-";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var probeKey = $"{ProbeKeyPrefix}{id}.txt";
        var payload = Encoding.UTF8.GetBytes(id);
        try
        {
            using (var content = new MemoryStream(payload))
            {
                await storage.SaveAsync(probeKey, content, "text/plain", cancellationToken);
            }

            await using (var stored = await storage.OpenReadAsync(probeKey, cancellationToken))
            {
                if (stored is null)
                {
                    return HealthCheckResult.Unhealthy("Media storage lost the probe file right after writing it.");
                }
            }

            await storage.DeleteAsync(probeKey, cancellationToken);
            return HealthCheckResult.Healthy("Media storage is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Media storage is not writable.", ex);
        }
    }
}