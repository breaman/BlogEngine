using BlogEngine.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Components.Pages;

public partial class Blog : ComponentBase
{
    private List<PostDto>? _posts = new();
    [Inject] private ApplicationDbContext DbContext { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var allPosts = await DbContext.Posts.AsNoTracking().Where(p => p.IsPublished)
            .OrderByDescending(p => p.PublishDate).ToListAsync();
        
        foreach (var post in allPosts)
        {
            _posts.Add(post.ToDto());
        }
    }
}