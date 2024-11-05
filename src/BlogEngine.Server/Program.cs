using BlogEngine.Data.Interfaces;
using BlogEngine.Server.Components;
using BlogEngine.Server.Components.Auth;
using BlogEngine.Server.Components.Email;
using BlogEngine.Server.Models;
using BlogEngine.Server.Services;
using BlogEngine.Shared.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedConstants = BlogEngine.Shared.Models.Constants;

var isMigrations = Environment.GetCommandLineArgs()[0].Contains("ef.dll");

var builder = WebApplication.CreateBuilder(args);

// plum in the service defaults for .net aspire
builder.AddServiceDefaults();

// Add "core" services to the container.
builder.Services.AddRazorComponents();
// End "core" services

// Add blazor auth stuff
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, PersistingServerAuthenticationStateProvider>();
// To ensure custom claims are added to new identity when principal is refreshed.
builder.Services.ConfigureOptions<ConfigureSecurityStampOptions>();
// End blazor auth stuff

// Add identity stuff
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.SignIn.RequireConfirmedEmail = true;
    })
    // AddRoles isn't added from the AddIdentityCore, so if you want to use roles, this must be explicitly added
    .AddRoles<Role>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
    
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SharedConstants.IsAdmin, p => p.RequireRole(SharedConstants.Administrator));
});
// End identity stuff

// Add FluentValidator stuff
builder.Services.AddTransient<IValidator<LoginDto>, LoginDtoValidator>();
builder.Services.AddTransient<IValidator<RegisterDto>, RegisterDtoValidator>();
// End FluentValidator stuff

// Add application stuff
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("blogenginedb"))
        .EnableSensitiveDataLogging());
builder.EnrichSqlServerDbContext<ApplicationDbContext>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
    
builder.Services.AddSingleton<IEnhancedEmailSender<User>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<IUserService, HttpUserService>();

builder.Services.Configure<BlogOptions>(builder.Configuration.GetSection("BlogOptions"));
// End application stuff

var app = builder.Build();

// plum in the default endpoints for .net aspire
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.Run();
