namespace BandManager.Data.Entities;

/// <summary>
/// One of a band member's role(s) within a specific band ("Bassist",
/// "Sound Engineer") - separate from BandRole (admin/user access level)
/// entirely: a person can hold several of these at once (e.g. Guitarist
/// + Marketing), which a single enum column can't represent, so this is
/// its own table rather than a field on BandMembership. Not a foreign
/// key into BandMembership's composite (UserId, BandId) key - just
/// separate FKs to User and Band - the caller (ProfileController) is
/// responsible for clearing these when a membership itself is removed,
/// same "join table enforces the real rule via the API, not a DB
/// constraint" split used elsewhere (see GigSetSong's doc comment).
/// </summary>
public class BandMemberRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;
    public required string Role { get; set; }
}
