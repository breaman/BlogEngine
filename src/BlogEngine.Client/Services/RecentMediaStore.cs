using System.Text.Json;

using BlogEngine.Shared.Contracts;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Services;

/// <summary>
/// The media picker's "Recently used" row (design 9.5, T2.11): the last few images inserted into posts, kept in the
/// browser's <c>localStorage</c> under <see cref="StorageKey"/>, newest first.
/// </summary>
/// <remarks>
/// Only a convenience: failures are logged and ignored, and entries are refreshed from the library before they are
/// shown, so a deleted or edited image never appears stale. Only call it from the browser.
/// </remarks>
public sealed class RecentMediaStore(IJSRuntime js, ILogger<RecentMediaStore> logger)
{
    /// <summary>The <c>localStorage</c> key.</summary>
    public const string StorageKey = "media:recent";

    /// <summary>How many images the row keeps.</summary>
    public const int MaxItems = 8;

    /// <summary>The ids of recently used items, newest first.</summary>
    public async Task<IReadOnlyList<int>> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            logger.LogWarning(ex, "Reading the recently used media failed.");
            return [];
        }
    }

    /// <summary>Moves <paramref name="item"/> to the front of the list.</summary>
    public async Task AddAsync(MediaItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var ids = (await LoadAsync()).Where(id => id != item.Id).Prepend(item.Id).Take(MaxItems).ToList();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(ids));
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Saving the recently used media failed.");
        }
    }
}
