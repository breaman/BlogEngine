using BlogEngine.Client.Services;
using BlogEngine.Shared.Security;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Validation;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore(options => options.AddBlogPolicies());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// Every API call goes through AntiforgeryHandler so mutating /api/admin requests carry the token (design 7.4).
builder.Services.AddScoped(sp =>
{
    var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
    var handler = new AntiforgeryHandler(sp.GetRequiredService<AntiforgeryStateProvider>(), baseAddress)
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler) { BaseAddress = baseAddress };
});

builder.Services.AddScoped<IToastService, ToastService>();

// FluentValidation validators for the admin forms, resolved by Blazilla's <FluentValidator />.
builder.Services.AddBlogValidators();

// Admin services over /api/admin; the server registers database-backed implementations of the same interfaces.
builder.Services.AddScoped<IPostAdminService, ClientPostAdminService>();
builder.Services.AddScoped<ITagService, ClientTagService>();

await builder.Build().RunAsync();
