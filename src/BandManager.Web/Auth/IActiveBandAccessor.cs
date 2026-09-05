namespace BandManager.Web.Auth;

/// <summary>
/// The "active band" a request is scoped to - kept server-side (session),
/// never trusted from a client-supplied value, so a regular User can never
/// point a request at a Band they don't belong to just by editing a
/// parameter. Set at login (if exactly one membership) or via the band
/// switcher endpoint.
/// </summary>
public interface IActiveBandAccessor
{
    Guid? GetActiveBandId();
    void SetActiveBandId(Guid bandId);
    void Clear();
}
