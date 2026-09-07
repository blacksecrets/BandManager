namespace BandManager.Data;

/// <summary>
/// The seed picklist of roles a band member can hold - a starting set
/// covering performance, crew/production, and business roles, meant to
/// be narrowed/expanded over time rather than treated as exhaustive.
/// Kept as a plain code list (not a SuperAdmin-managed DB table, unlike
/// Platforms/ContentTypes) since it's a much smaller, less
/// operationally-sensitive list - same tradeoff GearTypes.cs makes.
/// </summary>
public static class BandMemberRoles
{
    public static readonly string[] All =
    [
        // Performance
        "Lead Vocalist",
        "Backing Vocalist",
        "Guitarist",
        "Bassist",
        "Drummer",
        "Percussionist",
        "Keyboardist/Pianist",
        "Saxophonist",
        "Trumpet/Brass",
        "Violinist/Strings",
        "DJ",
        "Other Instrumentalist",
        // Songwriting/production
        "Songwriter",
        "Producer",
        // Crew/production
        "Sound Engineer",
        "Lighting Technician",
        "Stage Manager",
        "Gear Tech/Roadie",
        "Tour Manager",
        // Business
        "Band Manager",
        "Booking Agent",
        "Marketing",
        "Social Media",
        "Sales/Merchandise",
        "Publicist",
        "Photographer/Videographer",
        "Other"
    ];
}
