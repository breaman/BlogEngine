using BlogEngine.Server.Services.Email;

namespace BlogEngine.UnitTests.Server.Email;

/// <summary>
/// Tests <see cref="EmailOptions"/> (T4.21): the SMTP endpoint is read from the Aspire Mailpit connection string unless
/// a host is configured explicitly.
/// </summary>
public class EmailOptionsTests
{
    /// <summary>The Mailpit connection string gives the host and port; plain SMTP has no TLS on connect.</summary>
    [Test]
    public async Task ApplyConnectionString_ReadsSmtpEndpoint()
    {
        var options = new EmailOptions();

        options.ApplyConnectionString("Endpoint=smtp://localhost:1025");

        await Assert.That(options.IsConfigured).IsTrue();
        await Assert.That((options.SmtpHost, options.SmtpPort, options.UseSsl)).IsEqualTo(("localhost", 1025, false));
    }

    /// <summary>smtps means TLS from the start, on port 465 unless one is given.</summary>
    [Test]
    public async Task ApplyConnectionString_Smtps_UsesSsl()
    {
        var options = new EmailOptions();

        options.ApplyConnectionString("endpoint=smtps://mail.example.com");

        await Assert.That((options.SmtpHost, options.SmtpPort, options.UseSsl)).IsEqualTo(("mail.example.com", 465, true));
    }

    /// <summary>An explicitly configured host wins, and missing or unusable connection strings change nothing.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Endpoint=http://localhost:8025")]
    [Arguments("not a connection string ===")]
    public async Task ApplyConnectionString_IgnoresUnusableValues(string? connectionString)
    {
        var unset = new EmailOptions();
        var configured = new EmailOptions { SmtpHost = "smtp.example.com", SmtpPort = 587 };

        unset.ApplyConnectionString(connectionString);
        configured.ApplyConnectionString("Endpoint=smtp://localhost:1025");

        await Assert.That(unset.IsConfigured).IsFalse();
        await Assert.That((configured.SmtpHost, configured.SmtpPort)).IsEqualTo(("smtp.example.com", 587));
    }
}