using System.Net;

using BlogEngine.Data.Models;

using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Server.Services.Email;

/// <summary>
/// Sends the ASP.NET Core Identity account emails (email confirmation and password reset) through
/// <see cref="IEmailTransport"/>. It replaces the template's no-op sender (design 8.4).
/// </summary>
/// <remarks>
/// Links are HTML-encoded before they go into the message. While no SMTP server is configured
/// (<see cref="IsDelivering"/> is <see langword="false"/>), the register confirmation page shows the confirmation link
/// on screen instead, as the template did.
/// </remarks>
public sealed class IdentityEmailSender(IEmailTransport transport) : IEmailSender<User>
{
    /// <summary>Whether account emails actually reach the recipient.</summary>
    public bool IsDelivering => transport.IsDelivering;

    /// <inheritdoc />
    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
    {
        return SendLinkAsync(email, "Confirm your email", "Please confirm your account", confirmationLink);
    }

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
    {
        return SendLinkAsync(email, "Reset your password", "Please reset your password", resetLink);
    }

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
    {
        var code = WebUtility.HtmlEncode(resetCode);
        return transport.SendAsync(new EmailMessage(
            [email],
            "Reset your password",
            $"<p>Please reset your password using the following code: <strong>{code}</strong></p>",
            $"Please reset your password using the following code: {resetCode}"), CancellationToken.None);
    }

    /// <summary>Sends a message whose only content is one call to action with a link.</summary>
    private Task SendLinkAsync(string email, string subject, string action, string link)
    {
        var href = WebUtility.HtmlEncode(link);
        return transport.SendAsync(new EmailMessage(
            [email],
            subject,
            $"<p>{action} by <a href=\"{href}\">clicking here</a>.</p>",
            $"{action} by opening this link: {link}"), CancellationToken.None);
    }
}