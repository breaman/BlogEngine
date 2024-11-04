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
                Subtitle = "Just a quick subtitle",
                Slug = "just-a-test",
                Description = "This is a test.",
                PublishDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3))
            },
            new PostDto()
            {
                Title = "Another test",
                Slug = "another-test",
                Description = "This is just another test to get more than one entry in here.",
                PublishDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-2))
            },
            new PostDto()
            {
                Title = "Another test",
                Slug = "another-test",
                Description = "This is just another test to get more than one entry in here.",
                PublishDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-1))
            },
            new PostDto()
            {
                Title = "Another test",
                Subtitle = "And another quick subtitle",
                Slug = "another-test",
                Description = "This is just another test to get more than one entry in here.",
                PublishDate = DateOnly.FromDateTime(DateTime.Now)
            },
        };
        
        _posts = posts;
    }
}