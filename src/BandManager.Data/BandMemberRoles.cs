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
        "Lead Guitar",
        "Rhythm Guitar",
        "Bass",
        "Drums",
        "Percussion",
        "Lead Vocal",
        "Backup Vocal",
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

    // The subset of All that plausibly needs a per-song tuning tracked
    // (Repertoire's own Instrument Tunings list, BandInstrument.cs) -
    // deliberately excludes Songwriter/Producer and every Crew/Business
    // role, which have no meaningful "tuning." Used only for the
    // one-way auto-add convenience in ProfileController.SetMemberRoles -
    // never removes or renames a tuning-tracker entry, just seeds it.
    public static readonly string[] TuningEligible =
    [
        "Lead Guitar", "Rhythm Guitar", "Bass", "Drums", "Percussion",
        "Lead Vocal", "Backup Vocal", "Keyboardist/Pianist", "Saxophonist",
        "Trumpet/Brass", "Violinist/Strings", "DJ", "Other Instrumentalist"
    ];
}
