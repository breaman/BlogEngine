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
using BlogEngine.Server.Services.Comments;
using BlogEngine.Server.Services.Media;
using BlogEngine.Server.Services.Public;
using BlogEngine.Server.Storage;
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
    // Whole-response cache for feeds, the sitemap and robots.txt, evicted by tag like HybridCache (design 11).
    builder.Services.AddPublicOutputCache();

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
    builder.Services.AddScoped<IPageAdminService, ServerPageAdminService>();
    builder.Services.AddScoped<IPreviewLinkService, ServerPreviewLinkService>();
    builder.Services.AddScoped<ITagService, ServerTagService>();
    builder.Services.AddSingleton<PostHtmlSanitizer>();
    builder.Services.AddScoped<PostContentRenderer>();

    // Media library (design 9): files on disk under MediaStorage:RootPath (Q5), ImageSharp processing, and the
    // server side of the dual-service pattern. The storage health check shows up in /health (design 18).
    builder.Services.Configure<MediaStorageOptions>(builder.Configuration.GetSection(MediaStorageOptions.SectionName));
    builder.Services.AddSingleton<IMediaStorage, FileSystemMediaStorage>();
    builder.Services.AddSingleton<MediaProcessor>();
    builder.Services.AddScoped<ServerMediaService>();
    builder.Services.AddScoped<IMediaService>(sp => sp.GetRequiredService<ServerMediaService>());
    builder.Services.AddHealthChecks().AddCheck<MediaStorageHealthCheck>(MediaStorageHealthCheck.Name);

    // Comments (design 8): the public submission pipeline with its spam guard and rate limit, and the server side of
    // the moderation and dashboard services. The IP hash salt comes from Comments:IpHashSalt (the AppHost provides it).
    builder.Services.Configure<CommentOptions>(builder.Configuration.GetSection(CommentOptions.SectionName));
    builder.Services.AddSingleton<CommentIpHasher>();
    builder.Services.AddSingleton<CommentFormTimestamp>();
    builder.Services.AddSingleton<CommentHtmlSanitizer>();
    builder.Services.AddSingleton<CommentRenderer>();
    builder.Services.AddSingleton<SpamGuard>();
    builder.Services.AddScoped<CommentSubmissionService>();
    builder.Services.AddScoped<ICommentModerationService, ServerCommentModerationService>();
    builder.Services.AddScoped<IDashboardService, ServerDashboardService>();
    builder.Services.AddCommentRateLimiting();
    // Site search runs an uncached LIKE scan, so it is rate limited too (design 12.4, T4.9).
    builder.Services.AddRateLimiter(options => options.AddPolicy<string, SearchRateLimiting>(SearchRateLimiting.PolicyName));

    // Public site (static SSR, design 5.1, 11): cached read queries, cache eviction on writes, redirects and feeds.
    builder.Services.AddSingleton<PublicPostQueries>();
    builder.Services.AddSingleton<PublicCommentQueries>();
    builder.Services.AddSingleton<PublicSearchQueries>();
    builder.Services.AddSingleton<PublicPageQueries>();
    builder.Services.AddSingleton<CacheInvalidator>();
    builder.Services.AddSingleton<RedirectLookup>();
    builder.Services.AddSingleton<PreviewPostQuery>();
    builder.Services.AddSingleton<SyndicationFeedWriter>();
    // Evicts the public caches when a scheduled post's time comes (T4.1); a singleton so tests can drive it directly.
    builder.Services.AddSingleton<ScheduledPublishWatcher>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ScheduledPublishWatcher>());

    // Client services injected by admin pages; they only touch the browser after prerendering (T1.13).
    builder.Services.AddScoped<DraftBackupStore>();
    builder.Services.AddScoped<RecentMediaStore>();
    builder.Services.AddScoped<MediaLookupCache>();
    builder.Services.AddScoped<CommentCountNotifier>();

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
    // After routing, so the endpoint policies apply: comment POSTs on the post page, and site search.
    app.UseRateLimiter();
    app.UseOutputCache();
    app.MapStaticAssets();

    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(BlogEngine.Client._Imports).Assembly);

    app.MapAdditionalIdentityEndpoints();

    app.MapAdminApi();

    app.MapMediaEndpoints();
    app.MapFeedEndpoints();
    app.MapSitemapEndpoints();

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