using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace BlogEngine.Server.Services.Email;

/// <summary>
/// Sends email over SMTP with MailKit (design 8.4, C9). MailKit is used rather than <c>System.Net.Mail.SmtpClient</c>,
/// which Microsoft no longer recommends because it lacks modern protocol support.
/// </summary>
/// <remarks>
/// A connection is opened per message. The blog sends a handful of emails a day at most, so pooling connections isn't
/// worth holding one open.
/// </remarks>
public sealed partial class SmtpEmailTransport(IOptions<EmailOptions> options, ILogger<SmtpEmailTransport> logger) : IEmailTransport
{
    /// <summary>How long to wait for the server before giving up.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool IsDelivering => true;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        if (settings.SmtpHost is not { Length: > 0 } host)
        {
            throw new InvalidOperationException($"{nameof(SmtpEmailTransport)} is used without {EmailOptions.SectionName}:{nameof(EmailOptions.SmtpHost)}.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName ?? string.Empty, settings.FromAddress));
        foreach (var recipient in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(recipient));
        }

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            var security = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(host, settings.SmtpPort, security, cancellationToken);
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and (IOException or MailKit.ProtocolException
            or MailKit.CommandException or AuthenticationException or SslHandshakeException or System.Net.Sockets.SocketException
            or TimeoutException))
        {
            throw new EmailDeliveryException($"Sending \"{message.Subject}\" through {settings.SmtpHost}:{settings.SmtpPort} failed.", ex);
        }

        LogSent(logger, message.Subject, message.To.Count);
    }

    [LoggerMessage(EventId = 4002, EventName = "EmailSent", Level = LogLevel.Information,
        Message = "Email \"{Subject}\" sent to {RecipientCount} recipients")]
    private static partial void LogSent(ILogger logger, string subject, int recipientCount);
}