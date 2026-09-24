using System.Diagnostics;

using BlogEngine.AppHost;
using BlogEngine.ServiceDefaults;

var osArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("sql-password", "P@ssw0rd!")
    .InitiallyHidden();

var sqlServer = builder.AddSqlServer("sqlserver", dbPassword)
    .WithContainerName("blogengine-sqlserver");

if (osArch == System.Runtime.InteropServices.Architecture.Arm64
    && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
{
    sqlServer.WithImage("azure-sql-edge");
}

var db = sqlServer.WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Constants.DatabaseConnectionString);

var server = builder.AddProject<Projects.BlogEngine_Server>("server", "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck(Constants.HealthEndpointPath)
    .WithReference(db);

// Optional admin seeding (design 12.1). Only wired up when admin-email is configured, e.g.
//   dotnet user-secrets set "Parameters:admin-email" "you@example.com"
//   dotnet user-secrets set "Parameters:admin-password" "..."
// so a fresh clone isn't prompted for them and can use /setup instead. The server ignores them once any
// account exists.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:admin-email"]))
{
    var adminEmail = builder.AddParameter("admin-email");
    var adminPassword = builder.AddParameter("admin-password", secret: true);

    server.WithEnvironment("AdminSeed__Email", adminEmail)
        .WithEnvironment("AdminSeed__Password", adminPassword);
}

// Secret salt for commenter IP hashes (design 6.5, 12.3). Generated on the first run and kept in the AppHost's user
// secrets, so IP blocks keep matching across restarts. Set Parameters:comment-ip-salt to use your own.
var commentIpSalt = builder.AddParameter("comment-ip-salt", new GenerateParameterDefault { MinLength = 32, Special = false },
        secret: true, persist: true)
    .InitiallyHidden();
server.WithEnvironment("Comments__IpHashSalt", commentIpSalt);

// Media library files (design 9.3, Q5). The server runs as a project on this machine, so its default folder
// (src/BlogEngine.Server/App_Data/media) already survives restarts. To keep uploads somewhere else, such as a folder
// that is backed up, set it for the AppHost:
//   dotnet user-secrets set "MediaStorage:RootPath" "/path/to/media"
// A containerized deployment should mount a volume at the folder MediaStorage__RootPath points to.
if (builder.Configuration["MediaStorage:RootPath"] is { Length: > 0 } mediaRoot)
{
    server.WithEnvironment("MediaStorage__RootPath", Path.GetFullPath(mediaRoot, builder.AppHostDirectory));
}

var migrations = server.AddEFMigrations("ef-migrations")
    .WithMigrationsProject<Projects.BlogEngine_Data>()
    .RunDatabaseUpdateOnStart()
    .WithCommand("dotnet-tools", "Restore Tools", async (ExecuteCommandContext x) =>
    {
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = "dotnet",
            ArgumentList = { "tool", "restore" },
        });
        if (process is null) return CommandResults.Failure();
        await process.WaitForExitAsync(x.CancellationToken);
        return CommandResults.Success();
    }, new CommandOptions())
    .WaitFor(db);

server.WaitForCompletion(migrations);

builder.Build().Run();