var builder = DistributedApplication.CreateBuilder(args);

var db = builder
    .AddSqlServer("sqlserver", null, 1433)
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("blogenginedb");

var migration = builder.AddProject<Projects.BlogEngine_MigrationService>("migrations")
    .WithReference(db).WaitFor(db);

var blogEngine =
    builder.AddProject<Projects.BlogEngine_Server>("blogengine", config => config.LaunchProfileName = "https")
        .WithReference(db).WaitFor(db)
        .WaitForCompletion(migration);

builder.Build().Run();
