using Microsoft.AspNetCore.Authorization;

namespace BandManager.Web.Auth;

/// <summary>Any role, scoped to the current active Band - covers every
/// "available to all three levels" feature route.</summary>
public class BandMemberRequirement : IAuthorizationRequirement;

/// <summary>BandAdmin role in the active Band (or SuperAdmin) - Setup/
/// credential routes, band user management, platform enable/disable.</summary>
public class BandAdminRequirement : IAuthorizationRequirement;

/// <summary>SuperAdmin only - band onboarding, platform-level branding,
/// the SuperAdmin config screen.</summary>
public class SuperAdminRequirement : IAuthorizationRequirement;
