using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

/// <summary>
/// Manages the comment blocklist (design 6.5, 8.3, T3.9): keywords (+50 to the spam score), domains, emails and IP
/// hashes (straight to spam). Keywords, domains and emails can be added here; IP blocks come only from "Block
/// commenter", because the admin never sees raw IP addresses. Every kind can be removed.
/// </summary>
/// <remarks>
/// The form is validated by the shared <c>CommentBlockRequestValidator</c> through Blazilla's <c>&lt;FluentValidator /&gt;</c>;
/// the server validates again and its errors are shown as a toast.
/// </remarks>
public partial class CommentBlocklist : ComponentBase
{
    /// <summary>The kinds the form can add, with their labels.</summary>
    private static readonly IReadOnlyList<(CommentBlockKind Kind, string Label)> AddableKinds =
    [
        (CommentBlockKind.Keyword, "Keyword or phrase"),
        (CommentBlockKind.Domain, "Domain"),
        (CommentBlockKind.Email, "Email address")
    ];

    [Inject] private ICommentModerationService CommentService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;

    /// <summary>The blocklist, carried from prerendering into WebAssembly.</summary>
    [PersistentState]
    public List<CommentBlockDto>? Blocks { get; set; }

    private CommentBlockRequest Model { get; set; } = new();

    private ConfirmDialog _confirm = default!;
    private bool _saving;
    private int? _busyId;
    private string? _loadError;

    private string ValueLabel => Model.Kind switch
    {
        CommentBlockKind.Domain => "Domain, such as spam.example",
        CommentBlockKind.Email => "Email address",
        _ => "Keyword or phrase"
    };

    private string ValueHelp => Model.Kind switch
    {
        CommentBlockKind.Domain => "Comments linking to it, or from an email or website on it (subdomains too), go to spam.",
        CommentBlockKind.Email => "Comments from this address go straight to spam.",
        _ => "Comments whose name or text contains it get +50 on their spam score, which sends them to spam."
    };

    /// <summary>Loads the blocklist unless it was restored from prerendering.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Blocks is null)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            Blocks = [.. await CommentService.GetBlocksAsync()];
        }
        catch (HttpRequestException)
        {
            _loadError = "The blocklist couldn't be loaded. Check your connection and try again.";
        }
    }

    /// <summary>Adds the block and resets the form, keeping the chosen kind for the next one.</summary>
    private async Task AddAsync()
    {
        _saving = true;
        try
        {
            switch (await CommentService.AddBlockAsync(Model))
            {
                case CommentBlockSaved saved:
                    Toasts.ShowSuccess($"Blocked {KindLabel(saved.Block.Kind).ToLowerInvariant()} \"{saved.Block.Value}\".");
                    Model = new CommentBlockRequest { Kind = Model.Kind };
                    await LoadAsync();
                    break;
                case CommentBlockInvalid invalid:
                    Toasts.ShowWarning(string.Join(" ", invalid.Errors.SelectMany(e => e.Value)));
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Removes a block after confirming.</summary>
    private async Task DeleteAsync(CommentBlockDto block)
    {
        var value = block.Kind == CommentBlockKind.IpHash ? "this IP address" : $"\"{block.Value}\"";
        if (!await _confirm.ConfirmAsync("Remove block?", $"Comments matching {value} will no longer be treated as spam.", "Remove", "btn-danger"))
        {
            return;
        }

        _busyId = block.Id;
        try
        {
            if (!await CommentService.DeleteBlockAsync(block.Id))
            {
                Toasts.ShowWarning("That block was already removed.");
            }

            await LoadAsync();
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _busyId = null;
        }
    }

    private static string KindLabel(CommentBlockKind kind)
    {
        return kind switch
        {
            CommentBlockKind.Email => "Email",
            CommentBlockKind.IpHash => "IP address",
            CommentBlockKind.Domain => "Domain",
            _ => "Keyword"
        };
    }
}
