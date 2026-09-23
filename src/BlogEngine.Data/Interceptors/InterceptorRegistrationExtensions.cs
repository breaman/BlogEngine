using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogEngine.Data.Interceptors;

/// <summary>
/// Registers the data layer's EF Core interceptors and attaches them to a context in the right order.
/// </summary>
public static class InterceptorRegistrationExtensions
{
    /// <summary>
    /// Adds the save-pipeline interceptors to the service collection.
    /// </summary>
    /// <remarks>
    /// The interceptors are scoped because they depend on the scoped <c>IUserService</c>. That is safe
    /// for <c>AddDbContext</c> but not for <c>AddDbContextPool</c>, whose options are singletons.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDataInterceptors();
    /// builder.Services.AddDbContext&lt;ApplicationDbContext&gt;((sp, options) =&gt;
    ///     options.UseSqlServer(connectionString).AddDataInterceptors(sp));
    /// </code>
    /// </example>
    public static IServiceCollection AddDataInterceptors(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<FingerprintInterceptor>();
        services.AddScoped<AuditInterceptor>();

        return services;
    }

    /// <summary>
    /// Attaches the save-pipeline interceptors, resolved from <paramref name="serviceProvider"/>, to the context options.
    /// </summary>
    /// <remarks>
    /// EF Core runs interceptors in registration order, and the order here matters: soft deletes turn
    /// deletes into updates, fingerprinting then stamps those updates, and auditing records the final result.
    /// </remarks>
    public static DbContextOptionsBuilder AddDataInterceptors(this DbContextOptionsBuilder options,
        IServiceProvider serviceProvider)
    {
        return options.AddInterceptors(
            serviceProvider.GetRequiredService<SoftDeleteInterceptor>(),
            serviceProvider.GetRequiredService<FingerprintInterceptor>(),
            serviceProvider.GetRequiredService<AuditInterceptor>());
    }
}