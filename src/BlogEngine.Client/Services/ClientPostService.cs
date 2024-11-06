using BlogEngine.Shared.DTOs;

namespace BlogEngine.Client.Services;

public class ClientPostService : IPostService
{
    public Task<PostDto> GetPostByIdAsync(int postId)
    {
        throw new NotImplementedException();
    }
}