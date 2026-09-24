using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IPreviewLinkService"/> (A14): calls
/// <c>/api/admin/posts/{id}/preview-token(s)</c> and maps 404 to <see langword="null"/> or <see langword="false"/>.
/// </summary>
public sealed class ClientPreviewLinkService(HttpClient http) : IPreviewLinkService
{
    private const string BaseUri = "api/admin/posts";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreviewLinkDto>?> GetLinksAsync(int postId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{BaseUri}/{postId}/preview-tokens", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PreviewLinkDto>>(cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<PreviewLinkDto?> CreateAsync(int postId, CreatePreviewLinkRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync($"{BaseUri}/{postId}/preview-token", request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.BadRequest:
                var errors = await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken);
                throw new FluentValidation.ValidationException(string.Join(" ", errors.SelectMany(e => e.Value)));
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PreviewLinkDto>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(int postId, int linkId, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{BaseUri}/{postId}/preview-tokens/{linkId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}
