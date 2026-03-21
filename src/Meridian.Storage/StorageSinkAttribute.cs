using System.Reflection;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage;

/// <summary>
/// Marks a class as a storage sink plugin for automatic discovery and registration.
/// Classes decorated with this attribute will be discovered by <see cref="StorageSinkRegistry"/>
/// at startup when their assembly is scanned.
/// </summary>
/// <remarks>
/// This attribute is the storage-layer equivalent of the provider-layer
/// <c>DataSourceAttribute</c> (ADR-005), enabling the same plugin discovery
/// pattern for storage backends.
///
/// Once discovered, a sink can be activated via the <c>Storage.Sinks</c>
/// configuration list without any code changes to the composition root:
/// <code>
/// "Storage": {
///   "Sinks": ["jsonl", "parquet", "clickhouse"]
/// }
/// </code>
/// </remarks>
/// <example>
/// <code>
/// [StorageSink("clickhouse", "ClickHouse Storage")]
/// public sealed class ClickHouseSink : IStorageSink
/// {
///     // Implementation
/// }
/// </code>
/// </example>
[ImplementsAdr("ADR-005", "Attribute-based plugin discovery for storage sinks")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StorageSinkAttribute : Attribute
{
    /// <summary>
    /// Unique identifier for this sink (e.g., "jsonl", "parquet", "clickhouse").
    /// Used in the <c>Storage.Sinks</c> configuration list to activate the sink.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable display name (e.g., "JSONL Storage", "Apache Parquet Storage").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Optional description of the sink and its use case.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this sink is included in the default composition when no explicit
    /// <c>Sinks</c> list is provided. Default is <c>false</c>.
    /// </summary>
    public bool EnabledByDefault { get; set; } = false;

    /// <summary>
    /// Creates a new <see cref="StorageSinkAttribute"/>.
    /// </summary>
    /// <param name="id">Unique sink identifier used in configuration.</param>
    /// <param name="displayName">Human-readable display name.</param>
    public StorageSinkAttribute(string id, string displayName)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
}

/// <summary>
/// Metadata extracted from a <see cref="StorageSinkAttribute"/> for use by the
/// <see cref="StorageSinkRegistry"/> and composition root.
/// </summary>
public sealed record StorageSinkMetadata(
    string Id,
    string DisplayName,
    string? Description,
    bool EnabledByDefault,
    Type ImplementationType
)
{
    /// <summary>
    /// Creates metadata from a <see cref="StorageSinkAttribute"/> and its implementation type.
    /// </summary>
    public static StorageSinkMetadata FromAttribute(StorageSinkAttribute attr, Type implementationType)
    {
        return new StorageSinkMetadata(
            attr.Id,
            attr.DisplayName,
            attr.Description,
            attr.EnabledByDefault,
            implementationType
        );
    }
}

/// <summary>
/// Extension methods for <see cref="StorageSinkAttribute"/> discovery on <see cref="Type"/>.
/// </summary>
public static class StorageSinkAttributeExtensions
{
    /// <summary>
    /// Gets the <see cref="StorageSinkAttribute"/> from a type, if present.
    /// </summary>
    public static StorageSinkAttribute? GetStorageSinkAttribute(this Type type)
    {
        return Attribute.GetCustomAttribute(type, typeof(StorageSinkAttribute)) as StorageSinkAttribute;
    }

    /// <summary>
    /// Gets the <see cref="StorageSinkMetadata"/> from a type decorated with
    /// <see cref="StorageSinkAttribute"/>, or <c>null</c> if not decorated.
    /// </summary>
    public static StorageSinkMetadata? GetStorageSinkMetadata(this Type type)
    {
        var attr = type.GetStorageSinkAttribute();
        return attr != null ? StorageSinkMetadata.FromAttribute(attr, type) : null;
    }

    /// <summary>
    /// Returns <c>true</c> when the type is a concrete class that implements
    /// <see cref="IStorageSink"/> and is decorated with <see cref="StorageSinkAttribute"/>.
    /// </summary>
    public static bool IsStorageSinkPlugin(this Type type)
    {
        return type.GetStorageSinkAttribute() != null
            && typeof(IStorageSink).IsAssignableFrom(type)
            && !type.IsAbstract
            && !type.IsInterface;
    }
}
