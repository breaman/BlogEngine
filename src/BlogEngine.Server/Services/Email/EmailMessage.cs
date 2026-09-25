namespace BlogEngine.Server.Services.Email;

/// <summary>An email ready to send, with an HTML body and a plain-text alternative.</summary>
/// <param name="To">Recipient addresses.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body; everything in it that came from users must already be encoded or sanitized.</param>
/// <param name="TextBody">Plain-text body for clients that don't show HTML.</param>
public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody, string TextBody);
