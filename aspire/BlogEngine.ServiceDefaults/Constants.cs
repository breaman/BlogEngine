namespace BlogEngine.ServiceDefaults;

public static class Constants
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";
    public const string DatabaseConnectionString = "blogenginedb";

    /// <summary>
    /// Name of the development mail catcher (Mailpit) in the AppHost, and so of the connection string
    /// (<c>endpoint=smtp://host:port</c>) the server reads its SMTP endpoint from when <c>Email:SmtpHost</c> isn't set.
    /// </summary>
    public const string MailConnectionString = "mailpit";
}