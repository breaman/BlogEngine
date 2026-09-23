using System.Net;
using System.Text.RegularExpressions;

using BlogEngine.Data.Models;
using BlogEngine.Server.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers for tests that need accounts, logins and Blazor form posts against the in-memory server.
/// Tests that use the account helpers must be <c>[NotInParallel(TestConstraints.Users)]</c>.
/// </summary>
public static partial class IdentityTestHelper
{
    /// <summary>Password used for every test account.</summary>
    public const string Password = "Test-Passw0rd!";

    /// <summary>Email of the admin created by <see cref="ResetToSingleAdminAsync"/>.</summary>
    public const string AdminEmail = "admin@blogengine.test";

    /// <summary>Email of the non-admin created by <see cref="ResetToSingleAdminAsync"/>.</summary>
    public const string ReaderEmail = "reader@blogengine.test";

    /// <summary>
    /// A client that keeps cookies (so logins stick) and doesn't follow redirects (so tests can assert them).
    /// </summary>
    public static HttpClient CreateClient(BlogEngineWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    /// <summary>Deletes every user account, returning the site to its first-run state.</summary>
    public static async Task DeleteAllUsersAsync(BlogEngineWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Identity's user roles, claims, logins, tokens and passkeys cascade from the user row.
        await dbContext.Users.ExecuteDeleteAsync();
    }

    /// <summary>
    /// Resets the accounts to one admin (<see cref="AdminEmail"/>) plus one confirmed user without the
    /// Admin role (<see cref="ReaderEmail"/>), both with <see cref="Password"/>.
    /// </summary>
    public static async Task ResetToSingleAdminAsync(BlogEngineWebApplicationFactory factory)
    {
        await DeleteAllUsersAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AdminAccountService>();
        var (adminResult, _) = await accounts.CreateInitialAdminAsync(AdminEmail, Password, "Test Admin");
        EnsureSucceeded(adminResult);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var reader = new User { UserName = ReaderEmail, Email = ReaderEmail, EmailConfirmed = true, MemberSince = DateTimeOffset.UtcNow };
        EnsureSucceeded(await userManager.CreateAsync(reader, Password));
    }

    /// <summary>Logs the client in through the real login form.</summary>
    public static async Task LoginAsync(HttpClient client, string email, string password = Password)
    {
        using var response = await SubmitFormAsync(client, "/Account/Login", "login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false"
        });

        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException(
                $"Login as {email} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>
    /// Loads <paramref name="pagePath"/>, then posts the Blazor form <paramref name="formName"/> on it with
    /// the page's antiforgery token and the given fields, the way a browser submits an <c>EditForm</c>.
    /// </summary>
    public static async Task<HttpResponseMessage> SubmitFormAsync(
        HttpClient client, string pagePath, string formName, Dictionary<string, string> fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, pagePath);

        var form = new Dictionary<string, string>(fields)
        {
            ["_handler"] = formName,
            ["__RequestVerificationToken"] = token
        };

        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync(pagePath, content);
    }

    /// <summary>Reads the antiforgery request token from the hidden field a page renders.</summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pagePath)
    {
        using var response = await client.GetAsync(pagePath);
        var html = await response.Content.ReadAsStringAsync();

        var match = AntiforgeryFieldRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"GET {pagePath} returned {(int)response.StatusCode} without an antiforgery token.");
        }

        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    [GeneratedRegex("""<input[^>]*name="__RequestVerificationToken"[^>]*value="(?<value>[^"]+)""")]
    private static partial Regex AntiforgeryFieldRegex();
}
