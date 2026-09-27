using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// An in-memory <see cref="IMediaService"/> for component tests: lookups answer from <see cref="Items"/> and are
/// counted; everything else is unused by the components under test.
/// </summary>
internal sealed class FakeMediaService(params MediaLookupItem[] items) : IMediaService
{
    public List<MediaLookupItem> Items { get; } = [.. items];

    /// <summary>The ids of every lookup request, in order.</summary>
    public List<IReadOnlyCollection<string>> Lookups { get; } = [];

    public Task<IReadOnlyList<MediaLookupItem>> LookupAsync(IReadOnlyCollection<string> publicIds, CancellationToken cancellationToken = default)
    {
        Lookups.Add([.. publicIds]);
        return Task.FromResult<IReadOnlyList<MediaLookupItem>>([.. Items.Where(i => publicIds.Contains(i.PublicId))]);
    }

    public Task<PagedResult<MediaItemDto>> GetMediaAsync(MediaListQuery query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaItemDto?> GetMediaItemAsync(int id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaSaveResult> UpdateAsync(int id, MediaUpdateRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaSaveResult> EditAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaSaveResult> RevertAsync(int id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaSaveResult> SaveAsCopyAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaRenditionProgress> GetRenditionProgressAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaRenditionProgress> GenerateRenditionsAsync(int maxItems, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaDeleteResult> DeleteAsync(int id, bool force, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}