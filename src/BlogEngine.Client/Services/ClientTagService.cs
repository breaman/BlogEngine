using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="ITagService"/> over <c>/api/admin/tags</c>: autocomplete, and tag management
/// (design 6.4, O3), whose status codes are turned back into <see cref="TagResult"/> values so pages handle outcomes the
/// same way prerendered and in the browser.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is the one registered in <c>Program.cs</c>, whose <see cref="AntiforgeryHandler"/> adds
/// the antiforgery token to state-changing requests.
/// </remarks>
public sealed class ClientTagService(HttpClient http) : ITagService
{
    private const string BaseUri = "api/admin/tags";

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var uri = string.IsNullOrWhiteSpace(search)
            ? BaseUri
            : $"{BaseUri}?search={Uri.EscapeDataString(search)}";

        return await http.GetFromJsonAsync<List<TagDto>>(uri, cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagAdminDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<List<TagAdminDto>>($"{BaseUri}/all", cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<TagResult> UpdateAsync(int id, UpdateTagRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await http.PutAsJsonAsync($"{BaseUri}/{id}", request, cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagResult> MergeAsync(int id, int targetId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync($"{BaseUri}/{id}/merge/{targetId}", null, cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TagResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{BaseUri}/{id}", cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    /// <summary>
    /// Maps a response: 200 → <see cref="TagSaved"/>, 204 → <see cref="TagDeleted"/>, 404 → <see cref="TagNotFound"/>,
    /// 409 → <see cref="TagConflict"/> with the problem's detail, 400 → <see cref="TagInvalid"/>. Anything else throws.
    /// </summary>
    private static async Task<TagResult> ToResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                return TagResult.Deleted;
            case HttpStatusCode.NotFound:
                return TagResult.NotFound;
            case HttpStatusCode.Conflict:
                return new TagConflict(await ProblemResponseReader.ReadMessageAsync(response, cancellationToken));
            case HttpStatusCode.BadRequest:
                return new TagInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        var tag = await response.Content.ReadFromJsonAsync<TagAdminDto>(cancellationToken)
            ?? throw new InvalidOperationException("The tag API returned no tag.");

        return new TagSaved(tag);
    }
}