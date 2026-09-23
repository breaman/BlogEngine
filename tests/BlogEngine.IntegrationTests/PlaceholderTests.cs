using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.ServiceDefaults;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests;

/// <summary>
/// Smoke tests proving the integration test infrastructure is wired up: the server starts in memory,
/// serves requests, and the shared SQL Server container has every migration applied.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PlaceholderTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The in-memory server answers the health endpoint.</summary>
    [Test]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Constants.HealthEndpointPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>The fixture applied all migrations to the container database.</summary>
    [Test]
    public async Task Database_HasNoPendingMigrations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        await Assert.That(applied).IsNotEmpty();
        await Assert.That(pending).IsEmpty();
    }
}
