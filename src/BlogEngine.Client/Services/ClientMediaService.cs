using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IMediaService"/>: calls the <c>/api/admin/media</c> endpoints and turns
/// their status codes back into <see cref="MediaSaveResult"/> and <see cref="MediaDeleteResult"/> values, so pages
/// handle outcomes the same way prerendered and in the browser.
/// </summary>
/// <remarks>
/// Uploads don't go through this class: <c>media.js</c> sends them with <c>XMLHttpRequest</c>, which, unlike
/// <see cref="HttpClient"/> in the browser, reports upload progress.
/// </remarks>
public sealed class ClientMediaService(HttpClient http) : IMediaService
{
    /// <summary>Base URI of the media endpoints.</summary>
    public const string BaseUri = "api/admin/media";

    /// <inheritdoc />
    public async Task<PagedResult<MediaItemDto>> GetMediaAsync(MediaListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"page={query.Page}"),
            string.Create(CultureInfo.InvariantCulture, $"pageSize={query.PageSize}")
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        if (query.Unused)
        {
            parameters.Add("unused=true");
        }

        var result = await http.GetFromJsonAsync<PagedResult<MediaItemDto>>($"{BaseUri}?{string.Join('&', parameters)}", cancellationToken);
        return result ?? new PagedResult<MediaItemDto>([], 0, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<MediaItemDto?> GetMediaItemAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{BaseUri}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MediaItemDto>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaLookupItem>> LookupAsync(IReadOnlyCollection<string> publicIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicIds);
        if (publicIds.Count == 0)
        {
            return [];
        }

        var query = string.Join('&', publicIds.Select(id => $"ids={Uri.EscapeDataString(id)}"));
        return await http.GetFromJsonAsync<List<MediaLookupItem>>($"{BaseUri}/lookup?{query}", cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public Task<MediaSaveResult> UpdateAsync(int id, MediaUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Put, $"{BaseUri}/{id}", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MediaSaveResult> EditAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, $"{BaseUri}/{id}/edit", operations, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaSaveResult> RevertAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync($"{BaseUri}/{id}/revert", null, cancellationToken);
        return await ReadSaveResultAsync(response, $"POST {BaseUri}/{id}/revert", cancellationToken);
    }

    /// <inheritdoc />
    public Task<MediaSaveResult> SaveAsCopyAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, $"{BaseUri}/{id}/copy", operations, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaRenditionProgress> GetRenditionProgressAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<MediaRenditionProgress>($"{BaseUri}/renditions", cancellationToken)
            ?? new MediaRenditionProgress(0, 0);
    }

    /// <inheritdoc />
    public async Task<MediaRenditionProgress> GenerateRenditionsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(string.Create(CultureInfo.InvariantCulture, $"{BaseUri}/renditions?max={maxItems}"), null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MediaRenditionProgress>(cancellationToken) ?? new MediaRenditionProgress(0, 0);
    }

    /// <inheritdoc />
    public async Task<MediaDeleteResult> DeleteAsync(int id, bool force, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{BaseUri}/{id}{(force ? "?force=true" : string.Empty)}", cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return MediaDeleteResult.NotFound;
            case HttpStatusCode.Conflict:
                var posts = await response.Content.ReadFromJsonAsync<List<MediaUsageDto>>(cancellationToken) ?? [];
                return new MediaInUse(posts);
        }

        response.EnsureSuccessStatusCode();
        return MediaDeleteResult.Deleted;
    }

    /// <summary>
    /// Sends a JSON request and maps the response: 2xx → <see cref="MediaSaved"/>, 404 → <see cref="MediaNotFound"/>,
    /// 400 → <see cref="MediaInvalid"/>. Anything else throws.
    /// </summary>
    private async Task<MediaSaveResult> SendAsync<TBody>(HttpMethod method, string uri, TBody body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadSaveResultAsync(response, $"{method} {uri}", cancellationToken);
    }

    /// <summary>Maps a response to a save result (see <see cref="SendAsync"/>).</summary>
    private static async Task<MediaSaveResult> ReadSaveResultAsync(HttpResponseMessage response, string description,
        CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return MediaSaveResult.NotFound;
            case HttpStatusCode.BadRequest:
                return new MediaInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<MediaItemDto>(cancellationToken)
            ?? throw new InvalidOperationException($"{description} returned no media item.");

        return new MediaSaved(item);
    }
}
