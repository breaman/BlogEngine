using BlogEngine.Server.Components;

var builder = WebApplication.CreateBuilder(args);

// plum in the service defaults for .net aspire
builder.AddServiceDefaults();

// Add "core" services to the container.
builder.Services.AddRazorComponents();
// End "core" services

var app = builder.Build();

// plum in the default endpoints for .net aspire
app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.Run();
