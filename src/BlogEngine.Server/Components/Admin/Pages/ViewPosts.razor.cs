using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Components.Admin.Pages;

public partial class ViewPosts : ComponentBase
{
    private List<PostDto> _posts = new();
    [Inject] private ApplicationDbContext? DbContext { get; set; }
    
    protected override async Task OnInitializedAsync()
    {
        var posts = await DbContext.Posts.ToListAsync();
        foreach (var post in posts)
        {
            _posts.Add(post.ToDto());
        }
    }
}