using BlogEngine.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

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

    private PostDto _blogPost = new();
    
    [Inject] private ApplicationDbContext DbContext { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var postDate = new DateOnly(Year, Month, Day);
        var post = await DbContext.Posts.SingleOrDefaultAsync(p => p.Slug == Slug && p.IsPublished && p.PublishDate == postDate);
        // var post = await DbContext.Posts.SingleOrDefaultAsync(p => p.Slug == Slug && p.IsPublished);

        //var post = await DbContext.Posts.FirstOrDefaultAsync();
        _blogPost = post.ToDto();
    }
}