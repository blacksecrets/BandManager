namespace BandManager.Web.Auth;

/// <summary>
/// Every account's UserName is required to be a valid email address (it
/// doubles as the forgot-password destination - see AuthController.ForgotPassword,
/// which just uses whatever's saved as the user's Email, always kept in
/// sync with UserName). Checked wherever a username gets created or
/// changed: SetupController's bootstrap, ProfileController's add-user and
/// self-service username change.
/// </summary>
internal static class EmailValidation
{
    public static bool LooksLikeEmail(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address == value; }
        catch { return false; }
    }
}
