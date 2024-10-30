using BlogEngine.Server.DTOs;
using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components;

public partial class PostBlurb : ComponentBase
{
    [Parameter]
    public PostDto Post { get; set; }
}