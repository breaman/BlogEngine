using System.Buffers.Text;
using System.Security.Cryptography;

using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="IPreviewLinkService"/> (design 6.7, 12.2, A14): stores <see cref="PreviewToken"/>
/// rows, which <c>/preview/{token}</c> looks up.
/// </summary>
/// <remarks>
/// Tokens are 256 bits from <see cref="RandomNumberGenerator"/>, encoded as base64url so they fit in a URL unescaped,
/// which makes guessing one infeasible. Revoking deletes the row. Creating a link also removes the post's expired
/// links, so they don't pile up.
/// </remarks>
public sealed class ServerPreviewLinkService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IValidator<CreatePreviewLinkRequest> validator,
    ILogger<ServerPreviewLinkService> logger) : IPreviewLinkService
{
    /// <summary>Random bytes in a token: 256 bits (design 6.7).</summary>
    public const int TokenBytes = 32;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreviewLinkDto>?> GetLinksAsync(int postId, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Posts.AnyAsync(p => p.Id == postId, cancellationToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var tokens = await dbContext.PreviewTokens
            .AsNoTracking()
            .Where(t => t.PostId == postId && t.ExpiresOn > now)
            .OrderByDescending(t => t.CreatedOn)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        return [.. tokens.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<PreviewLinkDto?> CreateAsync(int postId, CreatePreviewLinkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        if (!await dbContext.Posts.AnyAsync(p => p.Id == postId, cancellationToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        await dbContext.PreviewTokens
            .Where(t => t.PostId == postId && t.ExpiresOn <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var token = new PreviewToken
        {
            PostId = postId,
            Token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes)),
            ExpiresOn = now.AddDays(request.ExpiresInDays)
        };
        dbContext.PreviewTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The token itself is a secret; only its id is logged.
        logger.LogInformation("Created preview link {PreviewLinkId} for post {PostId}, expiring {ExpiresOn}.", token.Id, postId, token.ExpiresOn);
        return ToDto(token);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(int postId, int linkId, CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.PreviewTokens
            .Where(t => t.Id == linkId && t.PostId == postId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Revoked preview link {PreviewLinkId} of post {PostId}.", linkId, postId);
        }

        return deleted > 0;
    }

    private static PreviewLinkDto ToDto(PreviewToken token)
    {
        return new PreviewLinkDto
        {
            Id = token.Id,
            Token = token.Token,
            Path = SitePaths.Preview(token.Token),
            ExpiresOn = token.ExpiresOn,
            CreatedOn = token.CreatedOn
        };
    }
}