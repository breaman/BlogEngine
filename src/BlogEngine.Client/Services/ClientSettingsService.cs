using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;
using FluentValidation.Results;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="ISettingsService"/> over <c>GET</c> and <c>PUT /api/admin/settings</c>
/// (design 13, T1.15).
/// </summary>
/// <remarks>
/// A 400 validation problem is turned back into the <see cref="ValidationException"/> the server implementation
/// throws, so the settings page handles a rejected save the same way whichever implementation it runs on.
/// </remarks>
public sealed class ClientSettingsService(HttpClient http) : ISettingsService
{
    private const string Uri = "api/admin/settings";

    /// <inheritdoc />
    public async Task<SiteSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<SiteSettingsDto>(Uri, cancellationToken)
            ?? throw new InvalidOperationException("The settings endpoint returned no settings.");
    }

    /// <inheritdoc />
    public async Task SaveAsync(SiteSettingsDto settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var response = await http.PutAsJsonAsync(Uri, settings, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errors = await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken);
            throw new ValidationException(errors.SelectMany(e => e.Value.Select(message => new ValidationFailure(e.Key, message))));
        }

        response.EnsureSuccessStatusCode();
    }
}