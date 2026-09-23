using BlogEngine.Client.Services;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="AntiforgeryHandler"/>: the token header is added to state-changing requests to the
/// app's own origin, and never to safe requests or other origins.
/// </summary>
public class AntiforgeryHandlerTests
{
    private const string Token = "request-token-value";
    private static readonly Uri BaseAddress = new("https://blog.example/");

    /// <summary>Every state-changing verb carries the token.</summary>
    [Test]
    [Arguments("POST")]
    [Arguments("PUT")]
    [Arguments("PATCH")]
    [Arguments("DELETE")]
    public async Task SendAsync_AddsTokenToMutatingRequests(string method)
    {
        var sent = await SendAsync(new HttpMethod(method), "api/admin/settings");

        await Assert.That(sent.Headers.GetValues(AntiforgeryHeaders.RequestToken)).IsEquivalentTo([Token]);
    }

    /// <summary>Safe verbs don't change state, so the token isn't sent.</summary>
    [Test]
    [Arguments("GET")]
    [Arguments("HEAD")]
    [Arguments("OPTIONS")]
    public async Task SendAsync_SkipsSafeRequests(string method)
    {
        var sent = await SendAsync(new HttpMethod(method), "api/admin/settings");

        await Assert.That(sent.Headers.Contains(AntiforgeryHeaders.RequestToken)).IsFalse();
    }

    /// <summary>The token must never leak to another host, even on a POST.</summary>
    [Test]
    [Arguments("https://evil.example/api/admin/settings")]
    [Arguments("http://blog.example/api/admin/settings")]
    [Arguments("https://blog.example:8443/api/admin/settings")]
    public async Task SendAsync_SkipsOtherOrigins(string url)
    {
        var sent = await SendAsync(HttpMethod.Post, url);

        await Assert.That(sent.Headers.Contains(AntiforgeryHeaders.RequestToken)).IsFalse();
    }

    /// <summary>With no token available (for example, not prerendered) the request goes out unchanged.</summary>
    [Test]
    public async Task SendAsync_WithoutToken_SendsRequestUnchanged()
    {
        var sent = await SendAsync(HttpMethod.Post, "api/admin/settings", token: null);

        await Assert.That(sent.Headers.Contains(AntiforgeryHeaders.RequestToken)).IsFalse();
    }

    /// <summary>A header set explicitly by the caller is left alone rather than duplicated.</summary>
    [Test]
    public async Task SendAsync_KeepsExistingHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/settings");
        request.Headers.Add(AntiforgeryHeaders.RequestToken, "caller-token");

        var sent = await SendAsync(request, Token);

        await Assert.That(sent.Headers.GetValues(AntiforgeryHeaders.RequestToken)).IsEquivalentTo(["caller-token"]);
    }

    /// <summary>Sends a request through the handler and returns what reached the network.</summary>
    private static async Task<HttpRequestMessage> SendAsync(HttpMethod method, string url, string? token = Token)
    {
        using var request = new HttpRequestMessage(method, url);
        return await SendAsync(request, token);
    }

    /// <summary>Sends a request through the handler and returns what reached the network.</summary>
    private static async Task<HttpRequestMessage> SendAsync(HttpRequestMessage request, string? token)
    {
        var capture = new CapturingHandler();
        var handler = new AntiforgeryHandler(new FakeAntiforgeryStateProvider(token), BaseAddress) { InnerHandler = capture };
        using var client = new HttpClient(handler) { BaseAddress = BaseAddress };

        using var response = await client.SendAsync(request);

        return capture.Request!;
    }

    /// <summary>Supplies a fixed token, as the persisted prerender state would.</summary>
    private sealed class FakeAntiforgeryStateProvider(string? token) : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken()
        {
            return token is null ? null : new AntiforgeryRequestToken(token, "__RequestVerificationToken");
        }
    }

    /// <summary>Terminal handler that records the outgoing request instead of sending it.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }
    }
}
