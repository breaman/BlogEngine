using System.Net;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BlogEngine.IntegrationTests.Security;

/// <summary>
/// Tests first-run admin creation (design 12.1, T0.15): the <c>/setup</c> page works exactly once, and
/// <see cref="AdminSeeder"/> seeds the admin from configuration only while no account exists.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class SetupTests(BlogEngineWebApplicationFactory factory)
{
    private const string SetupEmail = "owner@blogengine.test";

    /// <summary>With no accounts, <c>/setup</c> shows the admin form.</summary>
    [Test]
    public async Task Setup_WithNoUsers_ShowsForm()
    {
        await IdentityTestHelper.DeleteAllUsersAsync(factory);
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync("/setup");
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("Create admin account");
    }

    /// <summary>
    /// Submitting <c>/setup</c> creates a confirmed admin, signs them in and redirects to the dashboard;
    /// afterwards GET answers 404 and a POST can't create a second account.
    /// </summary>
    [Test]
    public async Task Setup_CreatesAdminOnce_ThenReturns404()
    {
        await IdentityTestHelper.DeleteAllUsersAsync(factory);
        using var client = IdentityTestHelper.CreateClient(factory);
        var token = await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/setup");

        using var created = await PostSetupAsync(client, token, SetupEmail);

        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(new Uri(client.BaseAddress!, created.Headers.Location!).AbsolutePath).IsEqualTo("/admin");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = await userManager.FindByEmailAsync(SetupEmail);
            await Assert.That(admin).IsNotNull();
            await Assert.That(admin!.DisplayName).IsEqualTo("Blog Owner");
            await Assert.That(admin.EmailConfirmed).IsTrue();
            await Assert.That(await userManager.IsInRoleAsync(admin, AppRoles.Admin)).IsTrue();
        }

        // The setup response signed the admin in.
        using var dashboard = await client.GetAsync("/admin");
        await Assert.That(dashboard.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var anonymous = IdentityTestHelper.CreateClient(factory);
        using var getAgain = await anonymous.GetAsync("/setup");
        await Assert.That(getAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // Posting the form again is refused and creates no second admin. (Blazor answers 400 because the
        // Not Found page it renders instead has no "setup" form to receive the post.)
        var replayToken = await IdentityTestHelper.GetAntiforgeryTokenAsync(anonymous, "/Account/Login");
        using var postAgain = await PostSetupAsync(anonymous, replayToken, "intruder@blogengine.test");
        await Assert.That((int)postAgain.StatusCode).IsBetween(400, 499);
        await Assert.That(await CountUsersAsync()).IsEqualTo(1);
    }

    /// <summary>The service refuses to create an admin once any account exists.</summary>
    [Test]
    public async Task CreateInitialAdmin_WhenUserExists_Fails()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AdminAccountService>();
        var (result, user) = await accounts.CreateInitialAdminAsync("second@blogengine.test", IdentityTestHelper.Password, "Second");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Select(e => e.Code)).Contains(AdminAccountService.AlreadySetUpErrorCode);
        await Assert.That(user).IsNull();
    }

    /// <summary>A rejected password creates nothing, leaving <c>/setup</c> available.</summary>
    [Test]
    public async Task CreateInitialAdmin_WithInvalidPassword_CreatesNothing()
    {
        await IdentityTestHelper.DeleteAllUsersAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AdminAccountService>();
        var (result, _) = await accounts.CreateInitialAdminAsync(SetupEmail, "123", "Owner");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(await CountUsersAsync()).IsEqualTo(0);
    }

    /// <summary>Configured seed values create the admin when no account exists.</summary>
    [Test]
    public async Task Seeder_WithNoUsers_CreatesAdmin()
    {
        await IdentityTestHelper.DeleteAllUsersAsync(factory);

        await RunSeederAsync(new AdminSeedOptions { Email = "seeded@blogengine.test", Password = IdentityTestHelper.Password });

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("seeded@blogengine.test");
        await Assert.That(admin).IsNotNull();
        await Assert.That(admin!.DisplayName).IsEqualTo("Admin");
        await Assert.That(await userManager.IsInRoleAsync(admin, AppRoles.Admin)).IsTrue();
    }

    /// <summary>Seeding never adds a second account once the site is set up.</summary>
    [Test]
    public async Task Seeder_WhenUsersExist_DoesNothing()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
        var before = await CountUsersAsync();

        await RunSeederAsync(new AdminSeedOptions { Email = "seeded@blogengine.test", Password = IdentityTestHelper.Password });

        await Assert.That(await CountUsersAsync()).IsEqualTo(before);
    }

    /// <summary>Without both an email and a password, seeding is skipped.</summary>
    [Test]
    public async Task Seeder_WithoutPassword_DoesNothing()
    {
        await IdentityTestHelper.DeleteAllUsersAsync(factory);

        await RunSeederAsync(new AdminSeedOptions { Email = "seeded@blogengine.test" });

        await Assert.That(await CountUsersAsync()).IsEqualTo(0);
    }

    /// <summary>Posts the setup form with a known display name.</summary>
    private static async Task<HttpResponseMessage> PostSetupAsync(HttpClient client, string token, string email)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "setup",
            ["__RequestVerificationToken"] = token,
            ["Input.DisplayName"] = "Blog Owner",
            ["Input.Email"] = email,
            ["Input.Password"] = IdentityTestHelper.Password,
            ["Input.ConfirmPassword"] = IdentityTestHelper.Password
        });

        return await client.PostAsync("/setup", content);
    }

    /// <summary>Runs the seeder's startup work with the given options against the test database.</summary>
    private async Task RunSeederAsync(AdminSeedOptions options)
    {
        var seeder = new AdminSeeder(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<AdminSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);
    }

    private async Task<int> CountUsersAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync();
    }
}