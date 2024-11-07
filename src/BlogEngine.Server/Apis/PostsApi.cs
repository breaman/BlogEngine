using System.Security.Claims;
using BlogEngine.Server.Models;
using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SharedConstants = BlogEngine.Shared.Models.Constants;

namespace BlogEngine.Server.Apis;

public static class PostsApi
{
    public static IEndpointConventionBuilder MapPostsApi(this IEndpointRouteBuilder endpoints)
    {
        var postsGroup = endpoints.MapGroup(SharedConstants.PostApiUrl);
        postsGroup.RequireAuthorization(SharedConstants.IsAdmin);

        postsGroup.MapGet("/{postId:int}", async (int postId, ApplicationDbContext dbContext) =>
        {
            return TypedResults.Ok(await dbContext.Posts.Where(p => p.Id == postId)
                .Select(p => p.ToDto())
                .SingleOrDefaultAsync());
        });
        
        postsGroup.MapPost("/create", async Task<Results<Created<PostDto>, ValidationProblem>> (PostDto dto,
            ClaimsPrincipal user, ApplicationDbContext dbContext, ILogger<PostDto> logger) =>
        {
            var validationProblem = Helpers.FetchUserId(out var userId, user, logger);
            if (validationProblem is not null) return validationProblem;
            
            var post = new Post();
            post.FromDto(dto, userId);
            
            dbContext.Posts.Add(post);
            
            try
            {
                await dbContext.SaveChangesAsync();
                logger.LogInformation("Successfully created Post {title}", dto.Title);

                dto.PostId = post.Id;
                return TypedResults.Created("", dto);
            }
            catch (Exception e)
            {
                logger.LogError("Unable to create Event {title} with the following exception: {exception}",
                    dto.Title, e);
                Dictionary<string, string[]> problems = new();
                problems.Add("error",
                    new[]
                    {
                        "An error occurred while trying to save the Event. If this continues please contact support."
                    });
                return TypedResults.ValidationProblem(problems);
            }
        });

        postsGroup.MapPut("/update/{postId:int}", async Task<Results<Ok<PostDto>, ValidationProblem>> (int postId,
            ClaimsPrincipal user, PostDto dto, ApplicationDbContext dbContext, ILogger<PostDto> logger) =>
        {
            var validationProblem = Helpers.FetchUserId(out var userId, user, logger);
            if (validationProblem is not null) return validationProblem;

            var existingPost =
                await dbContext.Posts.SingleOrDefaultAsync(p => p.Id == dto.PostId);

            if (existingPost is not null)
            {
                existingPost.FromDto(dto, userId);

                try
                {
                    await dbContext.SaveChangesAsync();
                    logger.LogInformation("Successfully updated Post {title}", dto.Title);
                    return TypedResults.Ok(dto);
                }
                catch (Exception e)
                {
                    logger.LogError("Unable to update Post {title} do to the following exception: {exception}",
                        dto.Title, e);
                    Dictionary<string, string[]> problems = new();
                    problems.Add("error",
                    [
                        "An error occurred while trying to update the Post. If this continues please contact support."
                    ]);
                    return TypedResults.ValidationProblem(problems);
                }
            }
            else
            {
                logger.LogError(
                    "Unable to update Post: {title} since it does not exist in the system",
                    dto.Title);
                Dictionary<string, string[]> problems = new();
                problems.Add("error",
                [
                    "Unable to update a Post that has does not exist in the system."
                ]);
                return TypedResults.ValidationProblem(problems);
            }
        });
        
        postsGroup.MapPost("/delete/{postId:int}", async (
            ApplicationDbContext dbContext,
            int postId) =>
        {
            var postToDelete = await dbContext.Posts.SingleOrDefaultAsync(c => c.Id == postId);
            dbContext.Posts.Remove(postToDelete);
            await dbContext.SaveChangesAsync();
            return TypedResults.Ok();
        });
        
        return postsGroup;
    }
}