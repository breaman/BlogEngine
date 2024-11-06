using BlogEngine.Client.Services;
using BlogEngine.Shared.DTOs;

namespace BlogEngine.Server.Services;

public class ServerPostService : IPostService
{
    public Task<PostDto> GetPostByIdAsync(int postId)
    {
        throw new NotImplementedException();
    }
}