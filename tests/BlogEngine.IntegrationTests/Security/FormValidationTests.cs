using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Components;

using FluentValidation;

namespace BlogEngine.IntegrationTests.Security;

/// <summary>
/// Tests that the server forms validate through FluentValidation and Blazilla's <c>&lt;FluentValidator /&gt;</c>:
/// the validators nested in the pages are registered, and an invalid static SSR post is rejected.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class FormValidationTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>
    /// Every validator declared in the server assembly, including the ones nested next to page input models,
    /// resolves from DI. <c>&lt;FluentValidator /&gt;</c> only logs a warning when it finds none, so a missing
    /// registration would otherwise silently turn a form's validation off.
    /// </summary>
    [Test]
    public async Task EveryServerValidator_IsRegistered()
    {
        var validatorTypes = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Select(t => (Type: t, Service: t.GetInterfaces().SingleOrDefault(IsValidatorInterface)))
            .Where(v => v.Service is not null)
            .ToList();

        await Assert.That(validatorTypes).IsNotEmpty();
        foreach (var (type, service) in validatorTypes)
        {
            await Assert.That(factory.Services.GetService(service!)?.GetType()).IsEqualTo(type);
        }
    }

    /// <summary>An invalid email re-renders the form with the validator's message instead of submitting.</summary>
    [Test]
    public async Task ForgotPassword_InvalidEmail_ShowsValidationMessage()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await IdentityTestHelper.SubmitFormAsync(client, "/Account/ForgotPassword", "forgot-password",
            new Dictionary<string, string> { ["Input.Email"] = "not-an-email" });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("'Email' is not a valid email address.");
    }

    private static bool IsValidatorInterface(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidator<>);
    }
}