using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components;

/// <summary>
/// The root component: the HTML document shared by every page, public and admin.
/// </summary>
public partial class App : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    /// <summary>Whether to ask search engines not to index any page (design 13).</summary>
    private bool _discourageSearchEngines;

    /// <summary>Reads the "discourage search engines" switch from the (cached) settings.</summary>
    protected override async Task OnInitializedAsync()
    {
        _discourageSearchEngines = (await SettingsService.GetAsync()).DiscourageSearchEngines;
    }
}
