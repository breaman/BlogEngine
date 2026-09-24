using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Server.Storage;
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

    /// <summary>A throwaway media folder for this test session, so uploads never touch the real App_Data.</summary>
    public string MediaRoot { get; } = Path.Combine(Path.GetTempPath(), "blogengine-integration-media", Guid.NewGuid().ToString("N"));

    /// <summary>Starts the host and migrates the database so tests see the production schema.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>Points the application's connection string at the test container and media storage at <see cref="MediaRoot"/>.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{Constants.DatabaseConnectionString}", SqlServer.ConnectionString);
        builder.UseSetting($"{MediaStorageOptions.SectionName}:{nameof(MediaStorageOptions.RootPath)}", MediaRoot);
        builder.UseSetting($"{CommentOptions.SectionName}:{nameof(CommentOptions.IpHashSalt)}", "integration-test-salt");

        // The in-memory server has no client address; let tests choose one, so rate limits are per test (T3.5).
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>());
    }

    /// <summary>Stops the host and removes the session's media folder.</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(MediaRoot))
        {
            Directory.Delete(MediaRoot, recursive: true);
        }
    }
}
