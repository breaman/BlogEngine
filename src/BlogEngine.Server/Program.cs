var builder = WebApplication.CreateBuilder(args);

// plum in the service defaults for .net aspire
builder.AddServiceDefaults();

var app = builder.Build();

// plum in the default endpoints for .net aspire
app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
