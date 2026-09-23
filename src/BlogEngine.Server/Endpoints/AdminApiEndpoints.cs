using BlogEngine.Shared.Security;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Maps the admin API (design 7.4): every endpoint lives under <see cref="RoutePrefix"/>, requires the
/// <see cref="AuthorizationPolicies.AdminOnly"/> policy and validates the antiforgery token on
/// state-changing requests. Feature areas add their endpoints to the group in <see cref="MapAdminApi"/>.
/// </summary>
public static class AdminApiEndpoints
{
    /// <summary>Route prefix shared by all admin API endpoints.</summary>
    public const string RoutePrefix = "/api/admin";

    /// <summary>Maps the admin API group and all of its endpoints.</summary>
    public static RouteGroupBuilder MapAdminApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(RoutePrefix)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            // Answer 401/403 instead of redirecting to the login page, even for endpoints that .NET doesn't
            // detect as API endpoints (for example a DELETE that returns no JSON).
            .DisableCookieRedirect()
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .WithTags("Admin");

        group.MapAdminSettingsEndpoints();
        group.MapAdminPostsEndpoints();
        group.MapAdminTagsEndpoints();

        return group;
    }
}
