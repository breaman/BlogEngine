using BlogEngine.ServiceDefaults;

using Microsoft.Data.SqlClient;

using Testcontainers.MsSql;

using TUnit.Core.Interfaces;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// A throwaway SQL Server container (Testcontainers) started once and shared by every test in the
/// session via <c>[ClassDataSource&lt;SqlServerContainer&gt;(Shared = SharedType.PerTestSession)]</c>.
/// Using real SQL Server rather than an in-memory provider keeps unique indexes, filtered indexes
/// and concurrency tokens behaving exactly as they do in production.
/// </summary>
public sealed class SqlServerContainer : IAsyncInitializer, IAsyncDisposable
{
    // Same image family Aspire uses for the development database.
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>
    /// Connection string for the application database inside the container. The database itself
    /// is created by EF Core when migrations are applied.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Starts the container; TUnit calls this before any dependent fixture is initialized.</summary>
    public async Task InitializeAsync()
    {
        await container.StartAsync();

        // The default connection string targets "master"; point it at a dedicated database instead.
        ConnectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = Constants.DatabaseConnectionString
        }.ConnectionString;
    }

    /// <summary>Stops and removes the container at the end of the test session.</summary>
    public ValueTask DisposeAsync()
    {
        return container.DisposeAsync();
    }
}
