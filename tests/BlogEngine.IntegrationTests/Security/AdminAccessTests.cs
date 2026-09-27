using System.Net;
using System.Net.Http.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.IntegrationTests.Security;

/// <summary>
/// Tests the admin shell and admin API plumbing (design 7.3, 7.4, 12.1, T0.16): <c>/admin</c> and
/// <c>/api/admin</c> require the <c>AdminOnly</c> policy, and state-changing API calls require the
/// antiforgery token.
/// </summary>
/// <remarks>Uses the settings endpoints as the representative admin API, so it also holds the settings lock.</remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.Users, TestConstraints.SiteSettings])]
public class AdminAccessTests(BlogEngineWebApplicationFactory factory)
{
    private const string SettingsApi = "/api/admin/settings";

    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>Anonymous visitors are sent to the login page, which returns them to the dashboard.</summary>
    [Test]
    public async Task Dashboard_Anonymous_RedirectsToLogin()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync("/admin");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        await Assert.That(location.AbsolutePath).IsEqualTo("/Account/Login").IgnoringCase();
        await Assert.That(Uri.UnescapeDataString(location.Query)).Contains("/admin");
    }

    /// <summary>A signed-in user without the Admin role is denied.</summary>
    [Test]
    public async Task Dashboard_NonAdmin_IsDenied()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.ReaderEmail);

        using var response = await client.GetAsync("/admin");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.AbsolutePath).IsEqualTo("/Account/AccessDenied").IgnoringCase();
    }

    /// <summary>The admin sees the dashboard inside the admin layout, with every section in the nav.</summary>
    [Test]
    public async Task Dashboard_Admin_ShowsAdminShell()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);

        using var response = await client.GetAsync("/admin");
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("<h1 class=\"h3 mb-0\">Dashboard</h1>");
        foreach (var section in new[] { "admin/posts", "admin/media", "admin/comments", "admin/tags", "admin/settings" })
        {
            await Assert.That(html).Contains($"href=\"{section}\"");
        }
    }

    /// <summary>The admin API answers 401 (not a login redirect) to anonymous callers.</summary>
    [Test]
    public async Task AdminApi_Anonymous_Returns401()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(SettingsApi);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>The admin API answers 403 to signed-in users without the Admin role.</summary>
    [Test]
    public async Task AdminApi_NonAdmin_Returns403()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.ReaderEmail);

        using var response = await client.GetAsync(SettingsApi);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>Safe requests don't need the antiforgery token.</summary>
    [Test]
    public async Task AdminApi_AdminGet_Succeeds()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);

        var settings = await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi);

        await Assert.That(settings).IsNotNull();
    }

    /// <summary>
    /// A state-changing admin request without the antiforgery token is rejected before the handler runs,
    /// even with a valid admin cookie (the CSRF case), and nothing is saved.
    /// </summary>
    [Test]
    public async Task AdminApi_MutationWithoutToken_IsRejected()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        var settings = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
        var originalTitle = settings.SiteTitle;
        settings.SiteTitle = "Forged by another site";

        using var response = await client.PutAsJsonAsync(SettingsApi, settings);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problem!.Title).IsEqualTo("Invalid antiforgery token");
        var reloaded = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
        await Assert.That(reloaded.SiteTitle).IsEqualTo(originalTitle);
    }

    /// <summary>With the token from the admin page in the header, the same request succeeds.</summary>
    [Test]
    public async Task AdminApi_MutationWithToken_Succeeds()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        var token = await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin");
        var original = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
        try
        {
            var changed = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
            changed.SiteTitle = "Saved through the admin API";

            using var response = await PutWithTokenAsync(client, changed, token);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            var reloaded = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
            await Assert.That(reloaded.SiteTitle).IsEqualTo("Saved through the admin API");
        }
        finally
        {
            using var restore = await PutWithTokenAsync(client, original, token);
        }
    }

    /// <summary>Invalid settings come back as a 400 problem rather than a server error.</summary>
    [Test]
    public async Task AdminApi_InvalidSettings_Returns400()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        var token = await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin");
        var settings = (await client.GetFromJsonAsync<SiteSettingsDto>(SettingsApi))!;
        settings.TimeZoneId = "Mars/Olympus_Mons";

        using var response = await PutWithTokenAsync(client, settings, token);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static Task<HttpResponseMessage> PutWithTokenAsync(HttpClient client, SiteSettingsDto settings, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, SettingsApi) { Content = JsonContent.Create(settings) };
        request.Headers.Add(AntiforgeryHeaders.RequestToken, token);
        return client.SendAsync(request);
    }
}