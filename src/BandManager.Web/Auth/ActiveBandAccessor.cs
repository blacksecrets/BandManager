namespace BandManager.Web.Auth;

public class ActiveBandAccessor(IHttpContextAccessor httpContextAccessor) : IActiveBandAccessor
{
    private const string SessionKey = "ActiveBandId";

    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HttpContext.");

    public Guid? GetActiveBandId()
    {
        var raw = HttpContext.Session.GetString(SessionKey);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public void SetActiveBandId(Guid bandId) =>
        HttpContext.Session.SetString(SessionKey, bandId.ToString());

    public void Clear() =>
        HttpContext.Session.Remove(SessionKey);
}
