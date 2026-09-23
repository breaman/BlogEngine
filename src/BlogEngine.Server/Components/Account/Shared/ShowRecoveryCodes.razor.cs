using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Account.Shared;

public partial class ShowRecoveryCodes : ComponentBase
{
    [Parameter]
    public string[] RecoveryCodes { get; set; } = [];

    [Parameter]
    public string? StatusMessage { get; set; }
}