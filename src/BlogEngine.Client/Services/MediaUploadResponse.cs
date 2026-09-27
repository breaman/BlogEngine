using System.Net;
using System.Text.Json;

using BlogEngine.Shared.Contracts;

namespace BlogEngine.Client.Services;

/// <summary>
/// Reads the response of a single-file upload to <c>POST /api/admin/media</c> (design 9.1), sent from JavaScript by the
/// media library's upload zone and by the editor's paste and drop upload (A10).
/// </summary>
/// <remarks>
/// A 200 carries one <see cref="MediaUploadResult"/> per file, which may itself be a rejection (for example an HEIC
/// file). Anything else failed before the file was looked at, and <see cref="DescribeFailure"/> explains it.
/// </remarks>
public static class MediaUploadResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The file's result from a response, or <see langword="null"/> when the request itself failed or the body can't be
    /// read.
    /// </summary>
    /// <param name="status">The HTTP status, or 0 for a network error.</param>
    /// <param name="body">The response body.</param>
    public static MediaUploadResult? ReadResult(int status, string body)
    {
        if (status != (int)HttpStatusCode.OK || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<MediaUploadResult>>(body, JsonOptions)?.FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A message for a request that didn't produce a file result.</summary>
    /// <param name="status">The HTTP status, or 0 for a network error.</param>
    /// <param name="body">The response body, which may be a problem document with a <c>detail</c>.</param>
    public static string DescribeFailure(int status, string body)
    {
        switch (status)
        {
            case 0:
                return "The upload failed. Check your connection and try again.";
            case (int)HttpStatusCode.Unauthorized:
            case (int)HttpStatusCode.Forbidden:
                return "Your session has expired. Sign in again and retry.";
            case (int)HttpStatusCode.RequestEntityTooLarge:
                return "The file is too large to upload.";
        }

        try
        {
            using var problem = JsonDocument.Parse(body);
            if (problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("detail", out var detail)
                && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not a problem document; use the generic message below.
        }

        return $"The upload failed (HTTP {status}).";
    }
}