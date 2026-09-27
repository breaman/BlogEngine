using System.Net;
using System.Net.Http.Json;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;

using FluentValidation;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="ClientSettingsService"/> (T1.15): it calls <c>/api/admin/settings</c> and reports a rejected
/// save as the same <see cref="ValidationException"/> the server implementation throws.
/// </summary>
public class ClientSettingsServiceTests
{
    /// <summary>Reads the settings from the API.</summary>
    [Test]
    public async Task GetAsync_ReturnsSettings()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, new SiteSettingsDto { SiteTitle = "Notes" }));

        var settings = await new ClientSettingsService(handler.CreateClient()).GetAsync();

        await Assert.That(settings.SiteTitle).IsEqualTo("Notes");
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/api/admin/settings");
    }

    /// <summary>A save is a PUT of the whole settings object.</summary>
    [Test]
    public async Task SaveAsync_PutsSettings()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await new ClientSettingsService(handler.CreateClient()).SaveAsync(new SiteSettingsDto { SiteTitle = "Notes" });

        var request = handler.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Put);
        await Assert.That(request.RequestUri!.AbsolutePath).IsEqualTo("/api/admin/settings");
    }

    /// <summary>Field errors from a validation problem become a <see cref="ValidationException"/>, keyed by property.</summary>
    [Test]
    public async Task SaveAsync_ValidationProblem_ThrowsValidationException()
    {
        var problem = new { title = "The settings are invalid.", errors = new Dictionary<string, string[]> { ["TimeZoneId"] = ["'Mars/Olympus_Mons' is not a known time zone."] } };
        var handler = new StubHttpHandler(_ => JsonResponse(HttpStatusCode.BadRequest, problem));

        var exception = await Assert.That(async () => await new ClientSettingsService(handler.CreateClient()).SaveAsync(new SiteSettingsDto()))
            .Throws<ValidationException>();

        var failure = exception!.Errors.Single();
        await Assert.That(failure.PropertyName).IsEqualTo("TimeZoneId");
        await Assert.That(failure.ErrorMessage).IsEqualTo("'Mars/Olympus_Mons' is not a known time zone.");
    }

    /// <summary>Unexpected failures are not disguised as validation errors.</summary>
    [Test]
    public async Task SaveAsync_ServerError_Throws()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.That(async () => await new ClientSettingsService(handler.CreateClient()).SaveAsync(new SiteSettingsDto()))
            .Throws<HttpRequestException>();
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }
}