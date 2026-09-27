using System.Collections.Concurrent;

using BlogEngine.Server.Services.Email;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the application's <see cref="IEmailTransport"/> for the test session: nothing is sent, and every message is
/// kept so tests can check what would have been.
/// </summary>
public sealed class CapturingEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    /// <inheritdoc />
    public bool IsDelivering => true;

    /// <summary>Every message sent so far in the session.</summary>
    public IReadOnlyCollection<EmailMessage> Sent => sent;

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> (5 seconds by default) for a message matching <paramref name="predicate"/>,
    /// since notifications are sent in the background; <see langword="null"/> if none arrives.
    /// </summary>
    public async Task<EmailMessage?> WaitForAsync(Func<EmailMessage, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (sent.FirstOrDefault(predicate) is { } message)
            {
                return message;
            }

            await Task.Delay(50);
        }

        return sent.FirstOrDefault(predicate);
    }
}