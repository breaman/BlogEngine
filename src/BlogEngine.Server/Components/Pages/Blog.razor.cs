using BlogEngine.Server.DTOs;
using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages;

public partial class Blog : ComponentBase
{
    private IEnumerable<PostDto> _posts = [];

    protected override void OnInitialized()
    {
        var posts = new List<PostDto>()
        {
            new PostDto()
            {
                Title = "Just a test",
                Slug = "just-a-test",
                Description = "This is a test.",
                PublishDate = DateTimeOffset.Now
            },
        };
        
        _posts = posts;
    }
}