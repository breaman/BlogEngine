using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Models;

public static class Helpers
{
    public static ValidationProblem? FetchUserId(out int managerId, ClaimsPrincipal claimsPrincipal, ILogger logger)
    {
        managerId = Convert.ToInt32(claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier));

        if (managerId == 0)
        {
            logger.LogError("Unable to find a UserId in the claims.");
            Dictionary<string, string[]> problems = new();
            problems.Add(ClaimTypes.NameIdentifier,
                ["UserId does not exist in the current set of claims, unable to complete operation"]);
            return TypedResults.ValidationProblem(problems);
        }

        return null;
    }
}