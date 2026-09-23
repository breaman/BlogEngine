using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IPostAdminService"/>: calls the <c>/api/admin/posts</c>
/// endpoints and turns their status codes back into <see cref="PostSaveResult"/> values, so pages handle
/// outcomes the same way whether they run prerendered (server implementation) or in the browser.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is the one registered in <c>Program.cs</c>, whose
/// <see cref="AntiforgeryHandler"/> adds the antiforgery token to state-changing requests.
/// </remarks>
public sealed class ClientPostAdminService(HttpClient http) : IPostAdminService
{
    private const string BaseUri = "api/admin/posts";

    /// <inheritdoc />
    public async Task<PagedResult<PostSummaryDto>> GetPostsAsync(PostListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>
        {
            $"status={query.Status}",
            string.Create(CultureInfo.InvariantCulture, $"page={query.Page}"),
            string.Create(CultureInfo.InvariantCulture, $"pageSize={query.PageSize}")
        };
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            parameters.Add($"tag={Uri.EscapeDataString(query.Tag)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        var result = await http.GetFromJsonAsync<PagedResult<PostSummaryDto>>(
            $"{BaseUri}?{string.Join('&', parameters)}", cancellationToken);

        return result ?? new PagedResult<PostSummaryDto>([], 0, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<PostEditDto?> GetPostAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{BaseUri}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PostEditDto>(cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostSaveResult> CreateAsync(PostEditDto post, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, BaseUri, post, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostSaveResult> UpdateAsync(int id, PostEditDto post, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Put, $"{BaseUri}/{id}", post, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostSaveResult> AutosaveAsync(int id, PostEditDto post, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, $"{BaseUri}/{id}/autosave", post, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostSaveResult> PublishAsync(int id, PublishPostRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, $"{BaseUri}/{id}/publish", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostSaveResult> UnpublishAsync(int id, UnpublishPostRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, $"{BaseUri}/{id}/unpublish", request, cancellationToken);
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

    /// <inheritdoc />
    public async Task<SlugCheckResult> CheckSlugAsync(SlugCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync($"{BaseUri}/slug-check", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SlugCheckResult>(cancellationToken)
            ?? throw new InvalidOperationException("The slug check returned no result.");
    }

    /// <summary>
    /// Sends a JSON request and maps the response: 2xx → <see cref="PostSaved"/>, 404 → <see cref="PostNotFound"/>,
    /// 409 → <see cref="PostConflict"/>, 400 → <see cref="PostInvalid"/>. Anything else throws.
    /// </summary>
    private async Task<PostSaveResult> SendAsync<TBody>(HttpMethod method, string uri, TBody body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return PostSaveResult.NotFound;
            case HttpStatusCode.Conflict:
                return PostSaveResult.Conflict;
            case HttpStatusCode.BadRequest:
                return new PostInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        var post = await response.Content.ReadFromJsonAsync<PostEditDto>(cancellationToken)
            ?? throw new InvalidOperationException($"{method} {uri} returned no post.");

        return new PostSaved(post);
    }
}
