using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="ITagService"/> over <c>GET /api/admin/tags</c>.
/// </summary>
public sealed class ClientTagService(HttpClient http) : ITagService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var uri = string.IsNullOrWhiteSpace(search)
            ? "api/admin/tags"
            : $"api/admin/tags?search={Uri.EscapeDataString(search)}";

        return await http.GetFromJsonAsync<List<TagDto>>(uri, cancellationToken) ?? [];
    }
}
