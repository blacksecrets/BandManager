using BandManager.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace BandManager.Web.Services;

/// <summary>
/// Creates a brand-new account with an admin-assigned temp password,
/// shared by every "add a user" surface (ProfileController's band users,
/// SuperAdminController's SuperAdmins) so the email/EmailConfirmed/
/// MustChangePassword conventions can't drift between them. Real email
/// delivery is deferred (see LoggingEmailSender) - the temp password still
/// goes out through IEmailSender either way, so swapping that
/// registration in Program.cs is the only change needed once a real
/// provider exists.
/// </summary>
public class UserProvisioningService(UserManager<ApplicationUser> userManager, IEmailSender emailSender)
{
    // "Temp-" guarantees an uppercase letter and a special character; the
    // timestamp guarantees at least one digit - together with the hex
    // suffix (which also supplies lowercase letters) this always satisfies
    // the password policy (Program.cs), not just almost-always.
    private static string GenerateTempPassword() =>
        $"Temp-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..24];

    public async Task<(bool Success, string? Error)> CreateAsync(string username, bool isSuperAdmin, string loginUrl)
    {
        var password = GenerateTempPassword();
        var user = new ApplicationUser
        {
            UserName = username,
            Email = username,
            EmailConfirmed = true, // an admin creating this account is the trusted path - see ProfileController.VerifyEmail's doc comment for the untrusted/self-service case
            MustChangePassword = true,
            IsSuperAdmin = isSuperAdmin,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return (false, string.Join("; ", result.Errors.Select(e => e.Description)));

        await emailSender.SendAsync(username, "Your BandManager account",
            $"""
            <p>An account was created for you on BandManager{(isSuperAdmin ? " with SuperAdmin access" : "")}.</p>
            <p>Temporary password: <strong>{password}</strong></p>
            <p><a href="{loginUrl}" style="display:inline-block;padding:10px 20px;background:#c0392b;color:#fff;text-decoration:none;border-radius:6px;font-weight:bold;">Log in to BandManager</a></p>
            <p>You'll be asked to choose your own password right away.</p>
            """);
        return (true, null);
    }
}
