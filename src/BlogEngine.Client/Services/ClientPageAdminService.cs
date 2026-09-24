using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IPageAdminService"/>: calls the <c>/api/admin/pages</c> endpoints and turns
/// their status codes back into <see cref="PageSaveResult"/> values, so the admin pages handle outcomes the same way
/// prerendered (server implementation) and in the browser.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is the one registered in <c>Program.cs</c>, whose <see cref="AntiforgeryHandler"/> adds
/// the antiforgery token to state-changing requests.
/// </remarks>
public sealed class ClientPageAdminService(HttpClient http) : IPageAdminService
{
    private const string BaseUri = "api/admin/pages";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageSummaryDto>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<List<PageSummaryDto>>(BaseUri, cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<PageEditDto?> GetPageAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{BaseUri}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PageEditDto>(cancellationToken);
    }

    /// <inheritdoc />
    public Task<PageSaveResult> CreateAsync(PageEditDto page, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, BaseUri, page, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PageSaveResult> UpdateAsync(int id, PageEditDto page, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Put, $"{BaseUri}/{id}", page, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PageSaveResult> PublishAsync(int id, CancellationToken cancellationToken = default)
    {
        return SendAsync<object?>(HttpMethod.Post, $"{BaseUri}/{id}/publish", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PageSaveResult> UnpublishAsync(int id, CancellationToken cancellationToken = default)
    {
        return SendAsync<object?>(HttpMethod.Post, $"{BaseUri}/{id}/unpublish", null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{BaseUri}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Sends a request (with a JSON body unless <paramref name="body"/> is <see langword="null"/>) and maps the response:
    /// 2xx → <see cref="PageSaved"/>, 404 → <see cref="PageNotFound"/>, 400 → <see cref="PageInvalid"/>. Anything else
    /// throws.
    /// </summary>
    private async Task<PageSaveResult> SendAsync<TBody>(HttpMethod method, string uri, TBody body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await http.SendAsync(request, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return PageSaveResult.NotFound;
            case HttpStatusCode.BadRequest:
                return new PageInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PageEditDto>(cancellationToken)
            ?? throw new InvalidOperationException($"{method} {uri} returned no page.");

        return new PageSaved(page);
    }
}
