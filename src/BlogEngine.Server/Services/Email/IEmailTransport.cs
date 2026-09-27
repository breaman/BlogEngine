namespace BlogEngine.Server.Services.Email;

/// <summary>
/// Delivers email (design 8.4, C9): SMTP through <see cref="SmtpEmailTransport"/> when it is configured, otherwise
/// <see cref="LoggingEmailTransport"/>, which only logs. Tests replace it to capture what would be sent.
/// </summary>
public interface IEmailTransport
{
    /// <summary>Whether messages actually leave the server.</summary>
    bool IsDelivering { get; }

    /// <summary>Sends <paramref name="message"/>.</summary>
    /// <exception cref="EmailDeliveryException">The server couldn't be reached or refused the message.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sending an email failed.</summary>
public sealed class EmailDeliveryException(string message, Exception innerException) : Exception(message, innerException);