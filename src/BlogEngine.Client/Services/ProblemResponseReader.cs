using System.Net.Http.Json;
using System.Text.Json;

namespace BlogEngine.Client.Services;

/// <summary>
/// Reads the field errors of an RFC 9457 problem document returned by the admin API, so the client
/// services report a 400 the same way the server implementations do.
/// </summary>
internal static class ProblemResponseReader
{
    /// <summary>Message used when a 400 carries neither field errors nor a readable title.</summary>
    public const string FallbackMessage = "The request was rejected.";

    /// <summary>
    /// Reads the field errors of a validation problem. Other 400 problems (such as a rejected antiforgery
    /// token) have no field errors, so their detail is reported under an empty key as a form-level error.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string[]>> ReadErrorsAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
        }
        catch (JsonException)
        {
            // Not a problem document; fall through to the generic message.
        }

        if (problem?.Errors is { Count: > 0 } errors)
        {
            return errors;
        }

        var message = problem?.Detail ?? problem?.Title ?? FallbackMessage;
        return new Dictionary<string, string[]> { [string.Empty] = [message] };
    }

    /// <summary>The parts of an RFC 9457 problem document this client reads.</summary>
    private sealed record ProblemResponse(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
