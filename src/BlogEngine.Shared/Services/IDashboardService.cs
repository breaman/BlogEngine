using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// The numbers and recent activity on the admin dashboard (design 4.5 O1, T3.10).
/// </summary>
/// <remarks>
/// The server implementation queries the database (prerendering and <c>GET /api/admin/dashboard</c>); the WebAssembly
/// implementation calls that endpoint.
/// </remarks>
public interface IDashboardService
{
    /// <summary>Loads the dashboard.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
