using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IDashboardService"/>: calls <c>GET /api/admin/dashboard</c>.
/// </summary>
public sealed class ClientDashboardService(HttpClient http) : IDashboardService
{
    /// <summary>URI of the dashboard endpoint.</summary>
    public const string Uri = "api/admin/dashboard";

    /// <inheritdoc />
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<DashboardSummaryDto>(Uri, cancellationToken) ?? new DashboardSummaryDto();
    }
}
