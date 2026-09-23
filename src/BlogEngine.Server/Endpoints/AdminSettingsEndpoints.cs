using System.ComponentModel.DataAnnotations;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

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

    /// <summary>Saves the settings; validation failures come back as a 400 problem.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> SaveSettingsAsync(
        SiteSettingsDto settings, ISettingsService settingsService, CancellationToken cancellationToken)
    {
        try
        {
            await settingsService.SaveAsync(settings, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (ValidationException ex)
        {
            return TypedResults.Problem(
                title: "The settings are invalid.",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
