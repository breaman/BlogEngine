using System.Data.Common;

namespace BlogEngine.Server.Services.Email;

/// <summary>
/// Outgoing email settings (design 8.4, C9), bound from the <see cref="SectionName"/> configuration section. Keep the
/// password in user secrets or an environment variable, never in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// In development the Aspire AppHost runs Mailpit and passes its SMTP endpoint as the <c>mailpit</c> connection string
/// (<c>endpoint=smtp://localhost:1025</c>), which <see cref="ApplyConnectionString"/> reads when no host is set here.
/// With neither, email isn't configured and messages are only logged (<see cref="LoggingEmailTransport"/>).
/// </remarks>
public sealed class EmailOptions
{
    /// <summary>Configuration section holding these options.</summary>
    public const string SectionName = "Email";

    /// <summary>The SMTP server; blank means email isn't configured.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>The SMTP port; 587 (submission with STARTTLS) by default.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Connect with TLS from the start (port 465) instead of upgrading with STARTTLS. When off, STARTTLS is used if the
    /// server offers it, which also works with a local catcher such as Mailpit that has no TLS.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>SMTP user name; blank for servers that don't authenticate, such as Mailpit.</summary>
    public string? UserName { get; set; }

    /// <summary>SMTP password.</summary>
    public string? Password { get; set; }

    /// <summary>The sender address.</summary>
    public string FromAddress { get; set; } = "blog@localhost";

    /// <summary>The sender's display name; the site title is used when blank.</summary>
    public string? FromName { get; set; }

    /// <summary>Whether an SMTP server is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);

    /// <summary>
    /// Takes the SMTP host and port from an Aspire connection string such as <c>endpoint=smtp://localhost:1025</c>,
    /// unless a host is configured already. Anything unparseable is ignored.
    /// </summary>
    /// <param name="connectionString">The connection string, or <see langword="null"/> when there is none.</param>
    public void ApplyConnectionString(string? connectionString)
    {
        if (IsConfigured || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return;
        }

        if (builder.TryGetValue("Endpoint", out var endpoint)
            && Uri.TryCreate(endpoint?.ToString(), UriKind.Absolute, out var uri)
            && uri.Scheme is "smtp" or "smtps")
        {
            SmtpHost = uri.Host;
            SmtpPort = uri.IsDefaultPort ? (uri.Scheme == "smtps" ? 465 : 25) : uri.Port;
            UseSsl = uri.Scheme == "smtps";
        }
    }
}