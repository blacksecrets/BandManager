namespace BandManager.Web.Services;

/// <summary>
/// Sends a transactional email (currently just password-reset links).
/// Deliberately minimal - one method, HTML body only - so swapping the
/// registered implementation in Program.cs for a real SMTP/SendGrid/etc.
/// sender later needs no change anywhere else.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
