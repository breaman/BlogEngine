using BlogEngine.Data.Models;
using BlogEngine.ServiceDefaults;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts <c>BlogEngine.Server</c> in memory against the shared SQL Server test container and applies
/// all EF Core migrations before the first test runs. Share it across the session with
/// <c>[ClassDataSource&lt;BlogEngineWebApplicationFactory&gt;(Shared = SharedType.PerTestSession)]</c>.
/// </summary>
public sealed class BlogEngineWebApplicationFactory : TestWebApplicationFactory<Program>, IAsyncInitializer
{
    /// <summary>The session-wide database container; TUnit initializes it before this factory.</summary>
    [ClassDataSource<SqlServerContainer>(Shared = SharedType.PerTestSession)]
    public required SqlServerContainer SqlServer { get; init; }

    /// <summary>Starts the host and migrates the database so tests see the production schema.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>Points the application's connection string at the test container.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{Constants.DatabaseConnectionString}", SqlServer.ConnectionString);
    }
}
