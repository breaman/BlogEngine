using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Data;

/// <summary>
/// Guards the EF Core model itself: it must build without any warnings (keys, relationships, query
/// filters) and the committed migrations must match it.
/// </summary>
/// <remarks>
/// The contexts use the application's service provider because the Identity model depends on the app's
/// <c>IdentityOptions</c> (schema version 3 adds the passkeys table); a bare context builds a different model.
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class ModelValidationTests(BlogEngineWebApplicationFactory factory)
{
    // Never opened: building the model doesn't connect.
    private const string UnusedConnectionString = "Server=unused;Database=unused;Integrated Security=true";

    /// <summary>
    /// Building the model with every warning escalated to an exception proves model validation is silent.
    /// </summary>
    [Test]
    public async Task Model_BuildsWithoutWarnings()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(UnusedConnectionString)
            .UseApplicationServiceProvider(factory.Services)
            .ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Throw))
            .Options;
        await using var dbContext = new ApplicationDbContext(options);

        await Assert.That(() => dbContext.Model).ThrowsNothing();
    }

    /// <summary>Every model change has been captured in a migration.</summary>
    [Test]
    public async Task Model_HasNoPendingChanges()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(dbContext.Database.HasPendingModelChanges()).IsFalse();
    }
}