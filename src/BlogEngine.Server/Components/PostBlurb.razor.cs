using BlogEngine.Shared.DTOs;

namespace BlogEngine.Server.Components;

public partial class PostBlurb : ComponentBase
{
    [Parameter]
    public PostDto Post { get; set; }
}