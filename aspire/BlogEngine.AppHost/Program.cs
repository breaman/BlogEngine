var builder = DistributedApplication.CreateBuilder(args);

var blogEngine =
    builder.AddProject<Projects.BlogEngine_Server>("blogengine", config => config.LaunchProfileName = "https");

builder.Build().Run();
