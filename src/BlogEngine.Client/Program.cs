using Blazored.Toast;
using BlogEngine.Client.Services;
using BlogEngine.Shared.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    });

builder.Services.AddBlazoredToast();

builder.Services.AddTransient<IValidator<PostDto>, PostDtoValidator>();
builder.Services.AddTransient<IPostService, ClientPostService>();

await builder.Build().RunAsync();