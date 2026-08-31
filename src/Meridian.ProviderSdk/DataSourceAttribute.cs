using System.Collections.Immutable;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Marks a class as a data source provider for automatic discovery and registration.
/// Classes decorated with this attribute will be automatically discovered and registered
/// with the DI container when using AddDataSources().
/// </summary>
/// <remarks>
/// This attribute is the foundation of ADR-005 (Attribute-Based Provider Discovery).
/// It enables automatic provider registration and metadata capture at startup.
/// </remarks>
/// <example>
/// <code>
/// [DataSource("alpaca", "Alpaca Markets", DataSourceType.Hybrid, DataSourceCategory.Broker, Priority = 10)]
/// public sealed class AlpacaDataSource : IRealtimeDataSource, IHistoricalDataSource
/// {
///     // Implementation
/// }
/// </code>
/// </example>
[ImplementsAdr("ADR-005", "Core attribute for provider discovery")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DataSourceAttribute : Attribute
{
    /// <summary>
    /// Stable provider-family identifier (e.g., "alpaca", "yahoo", "ibkr"). Implementations may
    /// share it only when their recognized service-contract capabilities are disjoint.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Type of data provided: Realtime, Historical, Hybrid, or Reference.
    /// </summary>
    public DataSourceType Type { get; }

    /// <summary>
    /// Category of the data source.
    /// </summary>
    public DataSourceCategory Category { get; }

    /// <summary>
    /// Priority for source selection (lower = higher priority, tried first).
    /// Default is 100.
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this source should be enabled by default when no explicit configuration is present.
    /// Default is true.
    /// </summary>
    public bool EnabledByDefault { get; set; } = true;

    /// <summary>
    /// Optional description of the data source.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Configuration section name for this source (defaults to Id).
    /// </summary>
    public string? ConfigSection { get; set; }

    /// <summary>
    /// Creates a new DataSourceAttribute.
    /// </summary>
    /// <param name="id">Stable provider-family identifier for this data source.</param>
    /// <param name="displayName">Human-readable display name.</param>
    /// <param name="type">Type of data provided.</param>
    /// <param name="category">Category of the data source.</param>
    public DataSourceAttribute(
        string id,
        string displayName,
        DataSourceType type,
        DataSourceCategory category)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Type = type;
        Category = category;
    }
}

/// <summary>
/// Metadata extracted from a DataSourceAttribute for registration and discovery.
/// </summary>
public sealed record DataSourceMetadata(
    string Id,
    string DisplayName,
    string? Description,
    DataSourceType Type,
    DataSourceCategory Category,
    int Priority,
    bool EnabledByDefault,
    string ConfigSection,
    Type ImplementationType
)
{
    /// <summary>
    /// Stable service-contract identities used to distinguish capabilities within one provider
    /// family ID. A provider may publish multiple implementations under the same ID when their
    /// capability keys are disjoint.
    /// </summary>
    public ImmutableArray<string> CapabilityKeys { get; init; } = [];

    /// <summary>
    /// Creates metadata from a DataSourceAttribute and its implementation type.
    /// </summary>
    public static DataSourceMetadata FromAttribute(DataSourceAttribute attr, Type implementationType)
    {
        return new DataSourceMetadata(
            attr.Id,
            attr.DisplayName,
            attr.Description,
            attr.Type,
            attr.Category,
            attr.Priority,
            attr.EnabledByDefault,
            attr.ConfigSection ?? attr.Id,
            implementationType)
        {
            CapabilityKeys = DataSourceCapabilityContracts.Resolve(implementationType)
        };
    }

    /// <summary>
    /// Whether this source is a real-time data source.
    /// </summary>
    public bool IsRealtime => Type is DataSourceType.Realtime or DataSourceType.Hybrid;

    /// <summary>
    /// Whether this source is a historical data source.
    /// </summary>
    public bool IsHistorical => Type is DataSourceType.Historical or DataSourceType.Hybrid;
}

/// <summary>
/// Extension methods for DataSourceAttribute discovery.
/// </summary>
public static class DataSourceAttributeExtensions
{
    /// <summary>
    /// Gets the DataSourceAttribute from a type, if present.
    /// </summary>
    public static DataSourceAttribute? GetDataSourceAttribute(this Type type)
    {
        return Attribute.GetCustomAttribute(type, typeof(DataSourceAttribute)) as DataSourceAttribute;
    }

    /// <summary>
    /// Gets the DataSourceMetadata from a type, if decorated with DataSourceAttribute.
    /// </summary>
    public static DataSourceMetadata? GetDataSourceMetadata(this Type type)
    {
        var attr = type.GetDataSourceAttribute();
        return attr != null ? DataSourceMetadata.FromAttribute(attr, type) : null;
    }

    /// <summary>
    /// Checks if a type is a data source (has DataSourceAttribute and is a concrete class).
    /// Accepts types implementing <see cref="IDataSource"/> or any recognized provider interface
    /// (IHistoricalDataProvider, IMarketDataClient, ISymbolSearchProvider, ICorporateActionProvider).
    /// </summary>
    public static bool IsDataSource(this Type type)
    {
        if (type.GetDataSourceAttribute() == null || type.IsAbstract || type.IsInterface)
            return false;

        return DataSourceCapabilityContracts.Resolve(type).Length > 0;
    }
}

/// <summary>
/// Resolves the service contracts that form a provider registration identity. Full names are used
/// for contracts owned by downstream modules so ProviderSdk does not reverse its dependency
/// direction merely to discover them.
/// </summary>
public static class DataSourceCapabilityContracts
{
    public const string RealtimeDataSource =
        "Meridian.Infrastructure.DataSources.IRealtimeDataSource";
    public const string HistoricalDataSource =
        "Meridian.Infrastructure.DataSources.IHistoricalDataSource";
    public const string MarketDataClient =
        "Meridian.Infrastructure.IMarketDataClient";
    public const string HistoricalDataProvider =
        "Meridian.Infrastructure.Adapters.Core.IHistoricalDataProvider";
    public const string SymbolSearchProvider =
        "Meridian.Infrastructure.Adapters.Core.ISymbolSearchProvider";
    public const string CorporateActionProvider =
        "Meridian.Infrastructure.Adapters.Core.ICorporateActionProvider";
    public const string OptionsChainProvider =
        "Meridian.Infrastructure.Adapters.Core.IOptionsChainProvider";
    public const string BrokerageGateway =
        "Meridian.Execution.Sdk.IBrokerageGateway";
    public const string GenericDataSource =
        "Meridian.Infrastructure.DataSources.IDataSource";

    private static readonly ImmutableHashSet<string> SpecificContracts =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            RealtimeDataSource,
            HistoricalDataSource,
            MarketDataClient,
            HistoricalDataProvider,
            SymbolSearchProvider,
            CorporateActionProvider,
            OptionsChainProvider,
            BrokerageGateway);

    /// <summary>
    /// Returns a deterministic set of recognized service-contract identities for a provider type.
    /// Generic <see cref="IDataSource"/> is used only when the implementation exposes no more
    /// specific recognized contract.
    /// </summary>
    public static ImmutableArray<string> Resolve(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        var interfaces = implementationType.GetInterfaces();
        var specific = interfaces
            .Select(static contract => contract.FullName)
            .Where(static name => name is not null && SpecificContracts.Contains(name))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToImmutableArray();
        if (!specific.IsEmpty)
        {
            return specific;
        }

        return typeof(IDataSource).IsAssignableFrom(implementationType)
            ? [GenericDataSource]
            : [];
    }
}
