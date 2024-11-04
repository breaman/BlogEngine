using BlogEngine.Server.DTOs;
using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages;

public partial class BlogPost : ComponentBase
{
    [Parameter]
    public int Year { get; set; }
    [Parameter]
    public int Month { get; set; }
    [Parameter]
    public int Day { get; set; }
    [Parameter]
    public string? Slug { get; set; }

    private PostDto? _blogPost = default!;

    protected override Task OnInitializedAsync()
    {
        // should fetch the post from the database here
        _blogPost = new PostDto()
        {
            Title = "This would be my title from the db",
            Subtitle = "Just a simple sub-title",
            PublishDate = DateOnly.FromDateTime(new DateTime(Year, Month, Day, 0, 0, 0)),
            Content =
                "This is going to the first of many of my awesome blog posts. The first part will really just be me blogging about the steps I took to create this site. I will also probably be putting some pieces of information up here around the IdentityServer stuff I'm doing as well so I don't lose it later on in life."
        };
        
        return Task.CompletedTask;
    }
}