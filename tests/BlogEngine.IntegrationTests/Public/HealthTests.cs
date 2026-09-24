using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.ServiceDefaults;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests <c>/health</c> (design 18, T2.1): it includes the media storage check, which passes when the media folder is
/// writable.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class HealthTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>With writable storage the app reports healthy.</summary>
    [Test]
    public async Task Health_WithWritableStorage_IsHealthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Constants.HealthEndpointPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
        await Assert.That(Directory.Exists(factory.MediaRoot)).IsTrue();
    }
}
