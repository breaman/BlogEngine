namespace BlogEngine.UnitTests.Client;

/// <summary>
/// An <see cref="HttpMessageHandler"/> for testing the WebAssembly API clients: answers every request with
/// the given function and records what was sent.
/// </summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>Base address of the clients under test.</summary>
    public static readonly Uri BaseAddress = new("https://blog.example/");

    /// <summary>The requests sent so far.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>An <see cref="HttpClient"/> over this handler with <see cref="BaseAddress"/>.</summary>
    public HttpClient CreateClient()
    {
        return new HttpClient(this) { BaseAddress = BaseAddress };
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
