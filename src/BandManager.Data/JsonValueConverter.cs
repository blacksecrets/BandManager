using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BandManager.Data;

/// <summary>Stores a List/Dictionary as a plain JSON text column - simplest
/// portable option, avoids depending on Npgsql-specific jsonb mapping
/// nuances for what's a small, rarely-queried-by-content field anyway.</summary>
internal static class JsonValueConverter
{
    public static ValueConverter<T?, string?> For<T>() where T : class => new(
        v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => v == null ? null : JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null));

    /// <summary>Same as For&lt;T&gt;, for a non-nullable required property -
    /// a separate overload rather than one nullable one, so EF's generated
    /// PropertyBuilder&lt;T&gt; (non-nullable) actually matches instead of
    /// warning (CS8620) on every required JSON-backed column.</summary>
    public static ValueConverter<T, string> ForRequired<T>() where T : class => new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null)!);

    /// <summary>JSON-round-trip equality - these fields are small and never
    /// compared in a hot path, so this is simpler and safer than a proper
    /// deep-equality implementation per collection/dictionary type.</summary>
    public static ValueComparer<T> ComparerRequired<T>() where T : class => new(
        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);

    public static ValueComparer<T?> Comparer<T>() where T : class => new(
        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
        v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null));
}
