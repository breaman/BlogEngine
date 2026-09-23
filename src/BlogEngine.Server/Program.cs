using System.Diagnostics;

using BlogEngine.Client.Services;
using BlogEngine.Data.Interceptors;
using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Server.Components;
using BlogEngine.Server.Components.Account;
using BlogEngine.Server.Components.Email;
using BlogEngine.Server.Endpoints;
using BlogEngine.Server.Services;
using BlogEngine.ServiceDefaults;
using BlogEngine.Shared.Security;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Validation;

using FluentValidation;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Serilog;

Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("Starting up");

var isMigrations = Environment.GetCommandLineArgs()[0].Contains("ef.dll");

try
{
    var builder = WebApplication.CreateBuilder(args);

    if (!isMigrations)
    {
        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration));
    }

    builder.AddServiceDefaults();

    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents()
        .AddAuthenticationStateSerialization();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();
    // All admin pages and admin API endpoints share the AdminOnly policy (design 12.1).
    builder.Services.AddAuthorization(options => options.AddBlogPolicies());

    // The WebAssembly admin sends the antiforgery token for /api/admin calls in this header (design 7.4).
    builder.Services.AddAntiforgery(options => options.HeaderName = AntiforgeryHeaders.RequestToken);

    builder.Services.AddDataInterceptors();
    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        options.UseSqlServer(builder.Configuration.GetConnectionString(Constants.DatabaseConnectionString))
            .EnableSensitiveDataLogging()
            .AddDataInterceptors(serviceProvider));
    builder.EnrichSqlServerDbContext<ApplicationDbContext>();
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    // In-memory (L1) cache for settings and public queries; tags let whole groups be evicted (design 11).
    builder.Services.AddHybridCache();

    builder.Services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedAccount = true;

            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
        // AddRoles isn't added from the AddIdentityCore, so if you want to use roles, this must be explicitly added
        .AddRoles<Role>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>();

    builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();
    builder.Services.AddScoped<IUserService, HttpUserService>();
    builder.Services.AddScoped<IToastService, ToastService>();
    builder.Services.AddScoped<ISettingsService, ServerSettingsService>();
    builder.Services.AddScoped<IPostAdminService, ServerPostAdminService>();
    builder.Services.AddScoped<ITagService, ServerTagService>();
    builder.Services.AddSingleton<PostHtmlSanitizer>();

    // Client services injected by admin pages; they only touch the browser after prerendering (T1.13).
    builder.Services.AddScoped<DraftBackupStore>();

    // FluentValidation validators, resolved by services and by Blazilla's <FluentValidator /> in forms: the shared
    // DTO validators, plus the validators nested next to the input models of the Identity and setup pages.
    builder.Services.AddBlogValidators();
    builder.Services.AddValidatorsFromAssemblyContaining<App>(ServiceLifetime.Singleton, includeInternalTypes: true);

    // First-run admin account: the /setup page, or seeding from the AdminSeed section (design 12.1).
    builder.Services.AddScoped<AdminAccountService>();
    builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection(AdminSeedOptions.SectionName));
    builder.Services.AddHostedService<AdminSeeder>();

    // The clock behind the public visibility rule and scheduling; tests replace it with a fake (design 6.3).
    builder.Services.AddSingleton(TimeProvider.System);

    // Add route configuration to enforce lowercase URLs for better SEO
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.LowercaseUrls = true;
        options.LowercaseQueryStrings = true;
        options.AppendTrailingSlash = false;
    });

    var app = builder.Build();

    app.MapDefaultEndpoints();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    // API callers need the real 401/403/404/409, not the not-found page re-executed with their PUT or POST.
    app.UseApiWithoutStatusCodePages();

    // Inside the status code pages middleware, so a 404 can become a stored 301 before the not-found page renders.
    app.UseMiddleware<RedirectFallbackMiddleware>();

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.MapStaticAssets();

    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(BlogEngine.Client._Imports).Assembly);

    app.MapAdditionalIdentityEndpoints();

    app.MapAdminApi();

    app.Run();
}
catch (Exception ex) when (ex.GetType().Name is not "StopTheHostException" &&
                           ex.GetType().Name is not "HostAbortedException")
{
    Log.Fatal(ex, "Unhandled exception.");
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}