namespace BlogEngine.Server.Services.Email;

/// <summary>
/// The <see cref="IEmailTransport"/> used when no SMTP server is configured: nothing is sent, and each message is logged
/// by subject only, because account emails contain sign-in links.
/// </summary>
public sealed partial class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    /// <inheritdoc />
    public bool IsDelivering => false;

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogNotSent(logger, message.Subject, message.To.Count);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 4001, EventName = "EmailNotConfigured", Level = LogLevel.Warning,
        Message = "Email isn't configured (set Email:SmtpHost), so \"{Subject}\" to {RecipientCount} recipients was not sent")]
    private static partial void LogNotSent(ILogger logger, string subject, int recipientCount);
}
