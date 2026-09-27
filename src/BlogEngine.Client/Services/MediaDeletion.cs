using BlogEngine.Client.Components;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.Client.Services;

/// <summary>
/// The delete flow shared by the media library and the media editor (design 9.6, T2.8): an unused image is deleted
/// after a plain confirmation; for an image that posts use, the server's answer lists them ("Used in 2 posts: …")
/// and deleting needs "Delete anyway".
/// </summary>
public static class MediaDeletion
{
    /// <summary>Most post titles listed in the warning before it says "and N more".</summary>
    private const int MaxTitlesListed = 5;

    /// <summary>Asks for confirmation, deletes, and reports the outcome with a toast.</summary>
    /// <returns><see langword="true"/> when the item is gone (deleted now, or already missing).</returns>
    public static async Task<bool> ConfirmAndDeleteAsync(ConfirmDialog confirm, IMediaService mediaService,
        IToastService toasts, MediaItemDto item)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(mediaService);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(item);

        if (item.UsageCount == 0
            && !await confirm.ConfirmAsync("Delete image?", $"{item.FileName} will be permanently deleted.", "Delete", "btn-danger"))
        {
            return false;
        }

        try
        {
            // Without force, the server refuses to delete an image in use and says which posts use it.
            var result = await mediaService.DeleteAsync(item.Id, force: false);
            if (result is MediaInUse inUse)
            {
                var confirmed = await confirm.ConfirmAsync(
                    "This image is in use",
                    $"{UsageText(inUse.Posts)} If you delete it anyway, those posts will no longer show it.",
                    "Delete anyway",
                    "btn-danger");
                if (!confirmed)
                {
                    return false;
                }

                result = await mediaService.DeleteAsync(item.Id, force: true);
            }

            if (result is MediaDeleted)
            {
                toasts.ShowSuccess($"{item.FileName} was deleted.");
            }
            else
            {
                toasts.ShowWarning($"{item.FileName} no longer exists.");
            }

            return true;
        }
        catch (HttpRequestException)
        {
            toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
            return false;
        }
    }

    /// <summary>"Used in 3 posts: A, B, C." with trashed posts marked, and long lists shortened.</summary>
    public static string UsageText(IReadOnlyCollection<MediaUsageDto> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);

        var noun = posts.Count == 1 ? "post" : "posts";
        var titles = posts.Take(MaxTitlesListed).Select(p => p.IsInTrash ? $"\"{p.Title}\" (in trash)" : $"\"{p.Title}\"");
        var more = posts.Count > MaxTitlesListed ? $" and {posts.Count - MaxTitlesListed} more" : string.Empty;

        return $"Used in {posts.Count} {noun}: {string.Join(", ", titles)}{more}.";
    }
}