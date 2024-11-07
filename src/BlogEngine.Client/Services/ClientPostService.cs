using System.Net.Http.Json;
using BlogEngine.Shared.DTOs;
using SharedConstants = BlogEngine.Shared.Models.Constants;

namespace BlogEngine.Client.Services;

public class ClientPostService(HttpClient Client) : IPostService
{
    public async Task<PostDto> GetPostByIdAsync(int postId)
    {
        return await Client.GetFromJsonAsync<PostDto>($"{SharedConstants.PostApiUrl}/{postId}");
    }
}