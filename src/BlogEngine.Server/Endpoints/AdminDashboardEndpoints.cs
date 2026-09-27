using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for the dashboard (design 4.5 O1, T3.10): <c>GET /dashboard</c>.
/// </summary>
public static class AdminDashboardEndpoints
{
    /// <summary>Maps the dashboard endpoint onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", GetSummaryAsync);

        return group;
    }

    /// <summary>The dashboard counts and recent activity.</summary>
    private static async Task<Ok<DashboardSummaryDto>> GetSummaryAsync(IDashboardService service, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.GetSummaryAsync(cancellationToken));
    }
}