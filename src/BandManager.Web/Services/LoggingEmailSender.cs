namespace BandManager.Web.Services;

/// <summary>
/// Stand-in IEmailSender that just logs the message instead of actually
/// sending it - lets the forgot-password flow be built and tested end to
/// end (grab the link from the console/logs) before a real email provider
/// is wired in. Swap the registration in Program.cs for a real
/// implementation (SMTP, SendGrid, etc.) once one is chosen; nothing else
/// in the app needs to change.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        logger.LogInformation(
            "EMAIL NOT ACTUALLY SENT (no email provider configured yet) - To: {ToEmail}, Subject: {Subject}\n{Body}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
