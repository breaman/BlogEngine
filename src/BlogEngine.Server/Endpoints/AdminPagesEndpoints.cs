using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for standalone pages (design 6.7, 7.4, A17): list, load, create, save, publish, unpublish and delete. Each
/// handler delegates to <see cref="IPageAdminService"/> and maps its <see cref="PageSaveResult"/> to HTTP: 200 with the
/// saved page, 404, or a 400 validation problem.
/// </summary>
public static class AdminPagesEndpoints
{
    /// <summary>Route name of <c>GET /pages/{id}</c>, used for the <c>Location</c> header on create.</summary>
    private const string GetPageRouteName = "AdminGetPage";

    /// <summary>Maps the page endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminPagesEndpoints(this RouteGroupBuilder group)
    {
        var pages = group.MapGroup("/pages");

        pages.MapGet("/", GetPagesAsync);
        pages.MapGet("/{id:int}", GetPageAsync).WithName(GetPageRouteName);
        pages.MapPost("/", CreatePageAsync);
        pages.MapPut("/{id:int}", (int id, PageEditDto page, IPageAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UpdateAsync(id, page, ct)));
        pages.MapPost("/{id:int}/publish", (int id, IPageAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.PublishAsync(id, ct)));
        pages.MapPost("/{id:int}/unpublish", (int id, IPageAdminService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UnpublishAsync(id, ct)));
        pages.MapDelete("/{id:int}", DeletePageAsync);

        return group;
    }

    /// <summary>Lists every page, drafts included.</summary>
    private static async Task<Ok<IReadOnlyList<PageSummaryDto>>> GetPagesAsync(IPageAdminService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.GetPagesAsync(cancellationToken));
    }

    /// <summary>Loads one page for the editor.</summary>
    private static async Task<Results<Ok<PageEditDto>, NotFound>> GetPageAsync(int id, IPageAdminService service,
        CancellationToken cancellationToken)
    {
        return await service.GetPageAsync(id, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : TypedResults.NotFound();
    }

    /// <summary>Creates a draft page; 201 with its location, or a 400 validation problem.</summary>
    private static async Task<Results<CreatedAtRoute<PageEditDto>, ValidationProblem>> CreatePageAsync(
        PageEditDto page, IPageAdminService service, CancellationToken cancellationToken)
    {
        return await service.CreateAsync(page, cancellationToken) switch
        {
            PageSaved saved => TypedResults.CreatedAtRoute(saved.Page, GetPageRouteName, new { id = saved.Page.Id }),
            PageInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Creating a page cannot result in {other.GetType().Name}.")
        };
    }

    /// <summary>Deletes a page permanently.</summary>
    private static async Task<Results<NoContent, NotFound>> DeletePageAsync(int id, IPageAdminService service,
        CancellationToken cancellationToken)
    {
        return await service.DeleteAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Maps a save outcome to its HTTP response.</summary>
    private static async Task<Results<Ok<PageEditDto>, NotFound, ValidationProblem>> ToHttpResultAsync(Task<PageSaveResult> operation)
    {
        return await operation switch
        {
            PageSaved saved => TypedResults.Ok(saved.Page),
            PageNotFound => TypedResults.NotFound(),
            PageInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Unknown page save result {other.GetType().Name}.")
        };
    }
}
