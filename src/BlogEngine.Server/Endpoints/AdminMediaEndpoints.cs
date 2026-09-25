using BlogEngine.Server.Services.Media;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Admin API for the media library (design 7.4, 9): upload, list, look up, metadata, edit, revert, save as copy,
/// delete, the original file for the image editor, and the rendition backfill. Handlers delegate to
/// <see cref="ServerMediaService"/>.
/// </summary>
public static class AdminMediaEndpoints
{
    /// <summary>Most files accepted in one upload request.</summary>
    public const int MaxFilesPerRequest = 20;

    /// <summary>Maps the media endpoints onto the admin API group.</summary>
    public static RouteGroupBuilder MapAdminMediaEndpoints(this RouteGroupBuilder group)
    {
        var media = group.MapGroup("/media");

        media.MapGet("/", GetMediaAsync);
        media.MapGet("/{id:int}", GetMediaItemAsync);
        media.MapGet("/lookup", LookupAsync);
        media.MapGet("/{id:int}/original", GetOriginalAsync);
        media.MapPost("/", UploadAsync);
        media.MapPut("/{id:int}", (int id, MediaUpdateRequest request, IMediaService service, CancellationToken ct) =>
            ToHttpResultAsync(service.UpdateAsync(id, request, ct)));
        media.MapPost("/{id:int}/edit", (int id, MediaEditOperations operations, IMediaService service, CancellationToken ct) =>
            ToHttpResultAsync(service.EditAsync(id, operations, ct)));
        media.MapPost("/{id:int}/revert", (int id, IMediaService service, CancellationToken ct) =>
            ToHttpResultAsync(service.RevertAsync(id, ct)));
        media.MapPost("/{id:int}/copy", (int id, MediaEditOperations operations, IMediaService service, CancellationToken ct) =>
            ToHttpResultAsync(service.SaveAsCopyAsync(id, operations, ct)));
        media.MapDelete("/{id:int}", DeleteAsync);

        // Responsive renditions for images stored before they existed or before the widths setting changed (T4.19):
        // how many are left, and a batch at a time so each request stays short.
        media.MapGet("/renditions", async (IMediaService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetRenditionProgressAsync(ct)));
        media.MapPost("/renditions", async (int? max, IMediaService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GenerateRenditionsAsync(max ?? ServerMediaService.MaxRenditionBatch, ct)));

        return group;
    }

    /// <summary>
    /// Lists the library: <c>GET /media?search=&amp;unused=&amp;page=&amp;pageSize=</c>. Every parameter is optional,
    /// which is why they are bound one by one (Minimal APIs would require the non-nullable properties of an
    /// <c>[AsParameters]</c> object).
    /// </summary>
    private static async Task<Ok<PagedResult<MediaItemDto>>> GetMediaAsync(string? search, bool? unused, int? page, int? pageSize,
        IMediaService service, CancellationToken cancellationToken)
    {
        var query = new MediaListQuery
        {
            Search = search,
            Unused = unused ?? false,
            Page = page ?? 1,
            PageSize = pageSize ?? MediaListQuery.DefaultPageSize
        };

        return TypedResults.Ok(await service.GetMediaAsync(query, cancellationToken));
    }

    /// <summary>Loads one item with the posts that use it.</summary>
    private static async Task<Results<Ok<MediaItemDto>, NotFound>> GetMediaItemAsync(int id, IMediaService service,
        CancellationToken cancellationToken)
    {
        return await service.GetMediaItemAsync(id, cancellationToken) is { } item
            ? TypedResults.Ok(item)
            : TypedResults.NotFound();
    }

    /// <summary>Metadata for rendering library images in the editor preview: <c>GET /media/lookup?ids=a&amp;ids=b</c>.</summary>
    private static async Task<Ok<IReadOnlyList<MediaLookupItem>>> LookupAsync(string[]? ids, IMediaService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.LookupAsync(ids ?? [], cancellationToken));
    }

    /// <summary>
    /// The untouched original, which the image editor shows because edits are always applied to it (design 9.2).
    /// Never cached by shared caches: it is only for the admin.
    /// </summary>
    private static async Task<IResult> GetOriginalAsync(int id, ServerMediaService service, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await service.OpenOriginalAsync(id, cancellationToken) is not { } original)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.CacheControl = "private, no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(original.Content, original.ContentType);
    }

    /// <summary>
    /// Uploads one or more images as <c>multipart/form-data</c> (design 9.1). Every file gets its own result, so a
    /// rejected file (not an image, too large) doesn't stop the rest; the response is 200 even when some fail.
    /// </summary>
    /// <remarks>
    /// The form is read by hand rather than bound to <c>IFormFileCollection</c>, so the size limits can follow the
    /// "max upload size" setting. Limits are set generously above the per-file limit, because an oversized file must
    /// still be read to report it on its own instead of failing the whole request.
    /// </remarks>
    private static async Task<Results<Ok<List<MediaUploadResult>>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        HttpRequest request, ServerMediaService service, ISettingsService settingsService, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return FilesProblem("Send the images as multipart/form-data.");
        }

        var settings = await settingsService.GetAsync(cancellationToken);
        var perFileLimit = Math.Max(FormOptions.DefaultMultipartBodyLengthLimit, settings.MaxUploadSizeMegabytes * 2L * 1024 * 1024);
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = perFileLimit * MaxFilesPerRequest;
        }

        request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
        {
            MultipartBodyLengthLimit = perFileLimit * MaxFilesPerRequest,
            ValueCountLimit = MaxFilesPerRequest * 2
        }));

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return TypedResults.Problem(
                title: "The upload couldn't be read.",
                detail: "The request is too large or malformed. Upload fewer or smaller files at a time.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (form.Files.Count == 0)
        {
            return FilesProblem("Choose at least one image to upload.");
        }

        if (form.Files.Count > MaxFilesPerRequest)
        {
            return FilesProblem($"Upload at most {MaxFilesPerRequest} files at a time.");
        }

        var results = new List<MediaUploadResult>(form.Files.Count);
        foreach (var file in form.Files)
        {
            await using var content = file.OpenReadStream();
            results.Add(await service.UploadAsync(file.FileName, content, file.Length, cancellationToken));
        }

        return TypedResults.Ok(results);
    }

    /// <summary>Deletes an item: 204, 404, or 409 with the posts that use it unless <c>?force=true</c>.</summary>
    private static async Task<Results<NoContent, NotFound, Conflict<IReadOnlyList<MediaUsageDto>>>> DeleteAsync(
        int id, bool? force, IMediaService service, CancellationToken cancellationToken)
    {
        return await service.DeleteAsync(id, force ?? false, cancellationToken) switch
        {
            MediaDeleted => TypedResults.NoContent(),
            MediaInUse inUse => TypedResults.Conflict(inUse.Posts),
            _ => TypedResults.NotFound()
        };
    }

    /// <summary>Maps a save outcome to its HTTP response.</summary>
    private static async Task<Results<Ok<MediaItemDto>, NotFound, ValidationProblem>> ToHttpResultAsync(Task<MediaSaveResult> operation)
    {
        return await operation switch
        {
            MediaSaved saved => TypedResults.Ok(saved.Item),
            MediaNotFound => TypedResults.NotFound(),
            MediaInvalid invalid => TypedResults.ValidationProblem(invalid.Errors),
            var other => throw new InvalidOperationException($"Unknown media save result {other.GetType().Name}.")
        };
    }

    private static ValidationProblem FilesProblem(string message)
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["files"] = [message] });
    }
}
