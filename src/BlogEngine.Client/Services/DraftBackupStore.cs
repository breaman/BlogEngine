using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Services;

/// <summary>
/// Keeps <see cref="PostDraftBackup"/> copies in the browser's <c>localStorage</c> under <c>draft:{postId}</c>
/// (design 10.2, T1.13); a post that hasn't been created yet uses <c>draft:new</c>.
/// </summary>
/// <remarks>
/// Storage can be unavailable (private browsing, a full quota) and the backup is only a safety net, so every
/// failure is logged and swallowed rather than interrupting the editor. Only call it from the browser, for
/// example in <c>OnAfterRenderAsync</c> or an event handler; there is no <c>localStorage</c> while prerendering.
/// </remarks>
public sealed class DraftBackupStore(IJSRuntime js, ILogger<DraftBackupStore> logger)
{
    /// <summary>Key prefix of every post backup.</summary>
    public const string KeyPrefix = "draft:";

    /// <summary>The storage key of a post's backup.</summary>
    public static string KeyFor(int postId)
    {
        return postId == 0 ? $"{KeyPrefix}new" : $"{KeyPrefix}{postId}";
    }

    /// <summary>Writes (or replaces) the backup of a post.</summary>
    public async Task SaveAsync(int postId, PostDraftBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", KeyFor(postId), JsonSerializer.Serialize(backup));
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Backing up post {PostId} to local storage failed.", postId);
        }
    }

    /// <summary>Reads a post's backup, or <see langword="null"/> when there is none or it can't be read.</summary>
    public async Task<PostDraftBackup?> LoadAsync(int postId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", KeyFor(postId));
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PostDraftBackup>(json);
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            logger.LogWarning(ex, "Reading the local backup of post {PostId} failed.", postId);
            return null;
        }
    }

    /// <summary>Deletes a post's backup.</summary>
    public async Task RemoveAsync(int postId)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", KeyFor(postId));
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Removing the local backup of post {PostId} failed.", postId);
        }
    }
}
