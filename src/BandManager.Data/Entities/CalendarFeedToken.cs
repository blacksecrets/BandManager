namespace BandManager.Data.Entities;

/// <summary>
/// One unguessable, anonymous-access token per user, powering the ICS
/// subscription feed (GET /api/calendar/feed/{token}.ics) - a calendar app
/// polling that URL has no login session, so this token (not the cookie
/// auth every other endpoint uses) is what scopes the request. Covers
/// every non-archived band the token's owner belongs to, not just one -
/// see CalendarFeedService.
/// </summary>
public class CalendarFeedToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Token { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
