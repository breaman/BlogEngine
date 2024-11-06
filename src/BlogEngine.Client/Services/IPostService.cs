using BlogEngine.Shared.DTOs;

namespace BlogEngine.Client.Services;

public interface IPostService
{
    public Task<PostDto> GetPostByIdAsync(int postId);
}