using BlogEngine.Client.Services;
using BlogEngine.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

public class ServerPostService(ApplicationDbContext DbContext) : IPostService
{
    public async Task<PostDto> GetPostByIdAsync(int postId)
    {
        return await DbContext.Posts.Where(p => p.Id == postId).Select(p => p.ToDto()).SingleOrDefaultAsync();
    }
}