using System.Net;
using System.Text.RegularExpressions;
using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Services;

/// <summary>
/// Stand-in IEmailSender that just logs the message instead of actually
/// sending it - lets the forgot-password flow be built and tested end to
/// end (grab the link from the console/logs) before a real email provider
/// is wired in. Swap the registration in Program.cs for a real
/// implementation (SMTP, SendGrid, etc.) once one is chosen; nothing else
/// in the app needs to change.
///
/// Also mirrors every "sent" email into an in-app Notification for every
/// SuperAdmin - the log is easy to lose track of (a temp password buried
/// in Docker logs), and until a real provider exists nobody would
/// otherwise see these at all. Unconditional (bypasses
/// NotificationPreference entirely) since this IS the delivery mechanism
/// during this phase, not an optional reminder a SuperAdmin could have
/// turned off. Revisit once a real IEmailSender implementation replaces
/// this one - a genuinely-delivered email doesn't need a safety-net copy.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger, ApplicationDbContext db) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        logger.LogInformation(
            "EMAIL NOT ACTUALLY SENT (no email provider configured yet) - To: {ToEmail}, Subject: {Subject}\n{Body}",
            toEmail, subject, htmlBody);

        var superAdminIds = await db.Users.Where(u => u.IsSuperAdmin).Select(u => u.Id).ToListAsync();
        if (superAdminIds.Count == 0) return;

        var plainBody = WebUtility.HtmlDecode(Regex.Replace(htmlBody, "<[^>]+>", " "));
        plainBody = Regex.Replace(plainBody, @"\s+", " ").Trim();
        var message = $"[Would send to {toEmail}] {subject}: {plainBody}";

        foreach (var id in superAdminIds)
        {
            db.Notifications.Add(new Notification { UserId = id, Kind = NotificationKind.OutboundEmailCopy, Message = message });
        }
        await db.SaveChangesAsync();
    }
}
