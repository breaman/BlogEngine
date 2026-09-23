using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for the site settings (design 7.4, 13): <c>GET /settings</c> and <c>PUT /settings</c>.
/// </summary>
public static class AdminSettingsEndpoints
{
    /// <summary>Maps the settings endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", GetSettingsAsync);
        group.MapPut("/settings", SaveSettingsAsync);

        return group;
    }

    /// <summary>Returns the current settings.</summary>
    private static async Task<Ok<SiteSettingsDto>> GetSettingsAsync(ISettingsService settingsService, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await settingsService.GetAsync(cancellationToken));
    }

    /// <summary>Saves the settings; validation failures come back as a 400 validation problem keyed by field.</summary>
    private static async Task<Results<NoContent, ValidationProblem>> SaveSettingsAsync(
        SiteSettingsDto settings, ISettingsService settingsService, CancellationToken cancellationToken)
    {
        try
        {
            await settingsService.SaveAsync(settings, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.ToArray());

            return TypedResults.ValidationProblem(errors, title: "The settings are invalid.");
        }
    }
}
