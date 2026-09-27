using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// Tag management at <c>/admin/tags</c> (design 6.4, 7.3, O3, T4.22): every tag with its usage counts, and row actions to
/// rename or describe a tag, merge it into another one, or delete it once no post uses it.
/// </summary>
/// <remarks>
/// <para>
/// The list loaded while prerendering is carried into WebAssembly with <see cref="PersistentStateAttribute"/>. It holds
/// every tag (a blog has hundreds at most), so the filter box narrows it in the browser without a round trip.
/// </para>
/// <para>
/// Editing and merging open a row under the tag rather than a dialog, so the tag stays visible for context. The server
/// decides whether a name or slug is free; its answers are shown in that row.
/// </para>
/// </remarks>
public partial class TagsList : ComponentBase
{
    [Inject] private ITagService TagService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;

    /// <summary>Every tag, alphabetically.</summary>
    [PersistentState]
    public List<TagAdminDto>? Items { get; set; }

    private ConfirmDialog _confirm = default!;
    private string? _loadError;
    private string _filter = string.Empty;
    private int? _busyTagId;

    private int? _editingId;
    private UpdateTagRequest? _editModel;
    private EditContext? _editContext;
    private ValidationMessageStore? _serverErrors;
    private string? _editError;
    private bool _saving;

    private int? _mergingId;
    private int _mergeTargetId;
    private string? _mergeError;

    /// <summary>The tags whose name or slug contains the filter text, case-insensitively.</summary>
    private IEnumerable<TagAdminDto> FilteredItems
    {
        get
        {
            var filter = _filter.Trim();
            return filter.Length == 0
                ? Items ?? []
                : (Items ?? []).Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || t.Slug.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string TagCountText => Items?.Count == 1 ? "1 tag" : $"{Items?.Count ?? 0:N0} tags";

    /// <summary>Loads the tags unless they were restored from the prerendered page.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Items is null)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            Items = [.. await TagService.GetAllAsync()];
        }
        catch (HttpRequestException)
        {
            _loadError = "The tags couldn't be loaded. Check your connection and try again.";
        }
    }

    /// <summary>Opens the edit row for a tag, closing any other edit or merge row.</summary>
    private void StartEdit(TagAdminDto tag)
    {
        CancelMerge();
        _editingId = tag.Id;
        _editError = null;
        _editModel = new UpdateTagRequest { Name = tag.Name, Slug = tag.Slug, Description = tag.Description };
        _editContext = new EditContext(_editModel);
        _serverErrors = new ValidationMessageStore(_editContext);
        _editContext.OnFieldChanged += (_, e) => _serverErrors.Clear(e.FieldIdentifier);
    }

    private void CancelEdit()
    {
        _editingId = null;
        _editModel = null;
        _editContext = null;
        _serverErrors = null;
        _editError = null;
    }

    /// <summary>
    /// Whether saving would move the tag to a new URL: a different slug was typed, or the slug was cleared and the name
    /// derives a different one. Clearing it when the name derives the same slug keeps the URL.
    /// </summary>
    private bool SlugChanging(TagAdminDto tag)
    {
        if (_editModel is null)
        {
            return false;
        }

        var typed = _editModel.Slug?.Trim();
        if (!string.IsNullOrEmpty(typed))
        {
            return !string.Equals(typed, tag.Slug, StringComparison.Ordinal);
        }

        // The server keeps the slug when it is the derived one, possibly with a -2, -3, … collision suffix.
        var derived = TagNormalizer.ToSlug(_editModel.Name);
        var keepsSlug = tag.Slug == derived
            || (tag.Slug.StartsWith(derived + "-", StringComparison.Ordinal)
                && int.TryParse(tag.Slug.AsSpan(derived.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var suffix)
                && suffix >= 2);
        return derived.Length > 0 && !keepsSlug;
    }

    /// <summary>Saves the edit row; field errors from the server appear under their fields, conflicts above the form.</summary>
    private async Task SaveEditAsync(TagAdminDto tag)
    {
        if (_editModel is null || _saving)
        {
            return;
        }

        _saving = true;
        _editError = null;
        _serverErrors?.Clear();
        try
        {
            switch (await TagService.UpdateAsync(tag.Id, _editModel))
            {
                case TagSaved saved:
                    Replace(saved.Tag);
                    CancelEdit();
                    Toasts.ShowSuccess($"\"{saved.Tag.Name}\" was saved.");
                    break;
                case TagConflict conflict:
                    _editError = conflict.Message;
                    break;
                case TagInvalid invalid:
                    ShowServerErrors(invalid.Errors);
                    break;
                case TagNotFound:
                    CancelEdit();
                    Toasts.ShowWarning($"\"{tag.Name}\" no longer exists.");
                    await LoadAsync();
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The tag couldn't be saved. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Opens the merge row for a tag, closing any other edit or merge row.</summary>
    private void StartMerge(TagAdminDto tag)
    {
        CancelEdit();
        _mergingId = tag.Id;
        _mergeTargetId = 0;
        _mergeError = null;
    }

    private void CancelMerge()
    {
        _mergingId = null;
        _mergeTargetId = 0;
        _mergeError = null;
    }

    /// <summary>Merges the tag into the chosen one after confirming, then reloads the list (both tags' counts change).</summary>
    private async Task MergeAsync(TagAdminDto tag)
    {
        if (Items?.Find(t => t.Id == _mergeTargetId) is not { } target)
        {
            return;
        }

        var posts = tag.PostCount + tag.TrashedPostCount;
        var confirmed = await _confirm.ConfirmAsync(
            $"Merge into \"{target.Name}\"?",
            $"{(posts == 1 ? "1 post" : string.Create(CultureInfo.CurrentCulture, $"{posts:N0} posts"))} tagged \"{tag.Name}\" will be tagged "
                + $"\"{target.Name}\" instead. \"{tag.Name}\" will be deleted, and {TagPaths.Tag(tag.Slug)} will redirect to {TagPaths.Tag(target.Slug)}.",
            "Merge",
            "btn-primary");
        if (!confirmed)
        {
            return;
        }

        _busyTagId = tag.Id;
        try
        {
            switch (await TagService.MergeAsync(tag.Id, target.Id))
            {
                case TagSaved saved:
                    CancelMerge();
                    Toasts.ShowSuccess($"\"{tag.Name}\" was merged into \"{saved.Tag.Name}\".");
                    await LoadAsync();
                    break;
                case TagNotFound:
                    CancelMerge();
                    Toasts.ShowWarning("One of the tags no longer exists. The list has been refreshed.");
                    await LoadAsync();
                    break;
                case TagConflict conflict:
                    _mergeError = conflict.Message;
                    break;
                case TagInvalid invalid:
                    _mergeError = string.Join(" ", invalid.Errors.SelectMany(e => e.Value));
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The tags couldn't be merged. Check your connection and try again.");
        }
        finally
        {
            _busyTagId = null;
        }
    }

    /// <summary>Deletes an unused tag after confirming.</summary>
    private async Task DeleteAsync(TagAdminDto tag)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Delete tag?",
            $"\"{tag.Name}\" isn't used by any post and will be deleted.",
            "Delete",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        _busyTagId = tag.Id;
        try
        {
            switch (await TagService.DeleteAsync(tag.Id))
            {
                case TagDeleted or TagNotFound:
                    Items?.RemoveAll(t => t.Id == tag.Id);
                    Toasts.ShowSuccess($"\"{tag.Name}\" was deleted.");
                    break;
                case TagConflict conflict:
                    // A post picked the tag up since the list was loaded; the refreshed counts show which.
                    Toasts.ShowWarning(conflict.Message);
                    await LoadAsync();
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The tag couldn't be deleted. Check your connection and try again.");
        }
        finally
        {
            _busyTagId = null;
        }
    }

    /// <summary>Puts a saved tag in place of its old row, keeping the list alphabetical.</summary>
    private void Replace(TagAdminDto tag)
    {
        if (Items is null)
        {
            return;
        }

        Items.RemoveAll(t => t.Id == tag.Id);
        Items.Add(tag);
        Items.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Shows field errors the server reported; errors not tied to a field go above the form.</summary>
    private void ShowServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        if (_editContext is null || _serverErrors is null)
        {
            return;
        }

        foreach (var (field, messages) in errors)
        {
            if (string.IsNullOrEmpty(field))
            {
                _editError = string.Join(" ", messages);
                continue;
            }

            _serverErrors.Add(_editContext.Field(field), messages);
        }

        _editContext.NotifyValidationStateChanged();
    }
}