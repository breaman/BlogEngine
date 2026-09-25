using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using FluentValidation;
using FluentValidation.Results;

namespace BlogEngine.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="ICommentModerationService"/>: calls <c>/api/admin/comments</c> and
/// <c>/api/admin/comment-blocks</c>, turning their status codes back into the values the server implementation returns,
/// so the moderation page behaves the same prerendered and in the browser.
/// </summary>
public sealed class ClientCommentModerationService(HttpClient http) : ICommentModerationService
{
    /// <summary>Base URI of the comment endpoints.</summary>
    public const string CommentsUri = "api/admin/comments";

    /// <summary>Base URI of the blocklist endpoints.</summary>
    public const string BlocksUri = "api/admin/comment-blocks";

    /// <inheritdoc />
    public async Task<PagedResult<CommentDto>> GetCommentsAsync(CommentListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var uri = string.Create(CultureInfo.InvariantCulture,
            $"{CommentsUri}?status={query.Status}&page={query.Page}&pageSize={query.PageSize}");
        var result = await http.GetFromJsonAsync<PagedResult<CommentDto>>(uri, cancellationToken);
        return result ?? new PagedResult<CommentDto>([], 0, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<CommentStatusCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<CommentStatusCounts>($"{CommentsUri}/counts", cancellationToken) ?? new CommentStatusCounts();
    }

    /// <inheritdoc />
    public async Task<bool> ModerateAsync(int id, CommentModerationAction action, CancellationToken cancellationToken = default)
    {
        using var response = action switch
        {
            CommentModerationAction.Approve => await http.PostAsync($"{CommentsUri}/{id}/approve", null, cancellationToken),
            CommentModerationAction.Reject => await http.PostAsync($"{CommentsUri}/{id}/reject", null, cancellationToken),
            CommentModerationAction.Spam => await http.PostAsync($"{CommentsUri}/{id}/spam", null, cancellationToken),
            CommentModerationAction.Delete => await http.DeleteAsync($"{CommentsUri}/{id}", cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown moderation action.")
        };

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<CommentReplyResult> ReplyAsync(int id, CommentReplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await http.PostAsJsonAsync($"{CommentsUri}/{id}/reply", request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return CommentReplyResult.NotFound;
            case HttpStatusCode.BadRequest:
                return new CommentReplyInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CommentReplied>(cancellationToken)
            ?? throw new InvalidOperationException($"POST {CommentsUri}/{id}/reply returned no reply.");
    }

    /// <inheritdoc />
    /// <exception cref="ValidationException">The server rejected the request (no comments, or too many).</exception>
    public async Task<int> ModerateManyAsync(CommentBulkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await http.PostAsJsonAsync($"{CommentsUri}/bulk", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errors = await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken);
            throw new ValidationException(errors.SelectMany(e => e.Value.Select(message => new ValidationFailure(e.Key, message))));
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BulkModerationResponse>(cancellationToken))?.Changed ?? 0;
    }

    /// <inheritdoc />
    public async Task<CommenterBlocked?> BlockCommenterAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync($"{CommentsUri}/{id}/block", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CommenterBlocked>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> EmptySpamAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync($"{CommentsUri}/empty-spam", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmptySpamResponse>(cancellationToken))?.Deleted ?? 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentBlockDto>> GetBlocksAsync(CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<List<CommentBlockDto>>(BlocksUri, cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<CommentBlockSaveResult> AddBlockAsync(CommentBlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await http.PostAsJsonAsync(BlocksUri, request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return new CommentBlockInvalid(await ProblemResponseReader.ReadErrorsAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        var block = await response.Content.ReadFromJsonAsync<CommentBlockDto>(cancellationToken)
            ?? throw new InvalidOperationException($"POST {BlocksUri} returned no block.");
        return new CommentBlockSaved(block);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBlockAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{BlocksUri}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}
