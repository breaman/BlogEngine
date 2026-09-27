using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// The tag index at <c>/tags</c> (design 7.1, P5): every tag with at least one visible post, with its post count.
/// </summary>
public partial class TagIndex : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;

    private IReadOnlyList<PublicTag> _tags = [];

    /// <summary>Loads the tags and their visible post counts.</summary>
    protected override async Task OnInitializedAsync()
    {
        _tags = await Queries.GetTagsAsync();
    }
}