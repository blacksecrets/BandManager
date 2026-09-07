using System.Text.Json.Serialization;

namespace BandManager.Data.Entities;

// String, not the default int, so the client (settings.js's generic form
// renderer) can switch on "Password"/"Radio"/etc. directly instead of
// needing to know this project's enum ordinal values. Serializes using
// the enum member's own PascalCase name (JsonConverterAttribute can't
// pass a naming-policy constructor arg to JsonStringEnumConverter<T>).
[JsonConverter(typeof(JsonStringEnumConverter<CredentialFieldType>))]
public enum CredentialFieldType { Text, Password, Url, Radio }

public record CredentialFieldOption(string Value, string Label, string? Hint = null);

/// <summary>A field is only required (and, in the UI, only shown) when
/// the named other field on the same form currently equals this value -
/// e.g. Instagram's igAccessToken only matters when mode == "standalone".
/// Evaluated against fields earlier in the same CredentialField list, so a
/// field a VisibleWhen depends on must be listed before it.</summary>
public record CredentialFieldVisibility(string Field, string Value);

/// <summary>
/// Describes one input on a platform's credential form - enough for both
/// the server (CredentialsController.Save's required/format validation)
/// and the client (settings.js's generic form renderer) to work from the
/// same definition instead of each hand-coding it per platform. Replaces
/// the old CredentialFields: List&lt;string&gt; (names only, so every
/// platform's actual markup - labels, types, Instagram's conditional
/// token field - was hand-authored HTML/JS per platform anyway).
/// </summary>
public record CredentialField(
    string Name,
    string Label,
    CredentialFieldType Type = CredentialFieldType.Text,
    bool Required = true,
    string? Placeholder = null,
    string? Hint = null,
    CredentialFieldVisibility? VisibleWhen = null,
    List<CredentialFieldOption>? Options = null,
    // False for a field that isn't a secret and doesn't belong in the
    // encrypted credential store - e.g. Website's siteBaseUrl/githubOwner/
    // githubRepo, which CredentialsController.Save routes onto the Band
    // entity's own columns instead. Still fully schema-driven for
    // rendering/validation purposes either way.
    bool StoreAsCredential = true,
    // A downstream constraint (Bandsintown's values get spliced into a JS
    // array literal on the site) rather than a general credential-form
    // concern, but expressed here so Save's validation loop stays generic
    // instead of special-casing platform == "bandsintown".
    bool ForbidQuotes = false);
