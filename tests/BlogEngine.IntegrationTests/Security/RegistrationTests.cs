using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Security;

/// <summary>
/// Tests the registration lockdown (design 12.1, T0.15): with <c>AllowRegistration = false</c> (the
/// default) the registration pages don't exist and no link points to them.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.SiteSettings)]
public class RegistrationTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>By default the registration pages answer 404, for GET and POST alike.</summary>
    [Test]
    [Arguments("/Account/Register")]
    [Arguments("/Account/RegisterConfirmation?email=admin@blogengine.test")]
    public async Task RegistrationPages_ByDefault_Return404(string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A post straight to the closed register form is refused and creates no account. (Blazor answers 400
    /// because the closed page renders no form to receive it.)
    /// </summary>
    [Test]
    public async Task RegisterPost_ByDefault_CreatesNoAccount()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        var token = await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "register",
            ["__RequestVerificationToken"] = token,
            ["Input.FirstName"] = "Eve",
            ["Input.LastName"] = "Intruder",
            ["Input.Email"] = "eve@blogengine.test",
            ["Input.Password"] = IdentityTestHelper.Password,
            ["Input.ConfirmPassword"] = IdentityTestHelper.Password
        });

        using var response = await client.PostAsync("/Account/Register", content);

        await Assert.That((int)response.StatusCode).IsBetween(400, 499);
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        await Assert.That(await userManager.FindByEmailAsync("eve@blogengine.test")).IsNull();
    }

    /// <summary>The nav and the login page don't link to the closed registration page.</summary>
    [Test]
    [Arguments("/")]
    [Arguments("/Account/Login")]
    public async Task Pages_ByDefault_HideRegisterLink(string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await client.GetStringAsync(path);

        await Assert.That(html).DoesNotContain("Account/Register");
    }

    /// <summary>Opening registration in the settings brings the page and its link back.</summary>
    [Test]
    public async Task Register_WhenAllowed_IsAvailable()
    {
        var original = await GetSettingsAsync();
        try
        {
            var open = await GetSettingsAsync();
            open.AllowRegistration = true;
            await SaveSettingsAsync(open);

            using var client = IdentityTestHelper.CreateClient(factory);
            using var response = await client.GetAsync("/Account/Register");
            var home = await client.GetStringAsync("/");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(home).Contains("Account/Register");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
    }

    private async Task<SiteSettingsDto> GetSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    private async Task SaveSettingsAsync(SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }
}
