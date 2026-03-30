using System.Reflection;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Bulk index of credential schemas declared via <see cref="RequiresCredentialAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AttributeCredentialResolver"/> is the lightweight path for resolving credentials
/// for a single provider type. <see cref="CredentialSchemaRegistry"/> is the companion
/// discovery/index surface for scenarios that need to enumerate many provider credential schemas
/// at once, such as UI catalogs.
/// </para>
/// <para>
/// Provider ID lookups are only available when a provider also declares
/// <see cref="DataSourceAttribute"/>. Providers without a data-source attribute remain available
/// through the type-based index.
/// </para>
/// </remarks>
public sealed class CredentialSchemaRegistry
{
    private readonly IReadOnlyList<CredentialSchema> _all;
    private readonly IReadOnlyDictionary<Type, CredentialSchema> _byProviderType;
    private readonly IReadOnlyDictionary<string, CredentialSchema> _byProviderId;

    private CredentialSchemaRegistry(IEnumerable<CredentialSchema> schemas)
    {
        _all = schemas
            .OrderBy(schema => schema.ProviderId ?? schema.ProviderType.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _byProviderType = _all.ToDictionary(schema => schema.ProviderType);
        _byProviderId = BuildProviderIdIndex(_all);
    }

    /// <summary>
    /// All discovered credential schemas.
    /// </summary>
    public IReadOnlyList<CredentialSchema> All => _all;

    /// <summary>
    /// Credential schemas indexed by provider implementation type.
    /// </summary>
    public IReadOnlyDictionary<Type, CredentialSchema> ByProviderType => _byProviderType;

    /// <summary>
    /// Credential schemas indexed by provider ID when <see cref="DataSourceAttribute"/> is present.
    /// </summary>
    public IReadOnlyDictionary<string, CredentialSchema> ByProviderId => _byProviderId;

    /// <summary>
    /// Builds a registry from the supplied provider types.
    /// </summary>
    public static CredentialSchemaRegistry FromTypes(IEnumerable<Type> providerTypes)
    {
        ArgumentNullException.ThrowIfNull(providerTypes);

        var schemas = providerTypes
            .Where(type => type is not null)
            .Distinct()
            .Select(CreateSchema)
            .Where(schema => schema is not null)
            .Cast<CredentialSchema>();

        return new CredentialSchemaRegistry(schemas);
    }

    /// <summary>
    /// Discovers credential schemas from one or more assemblies.
    /// </summary>
    public static CredentialSchemaRegistry DiscoverFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        }

        var providerTypes = assemblies
            .SelectMany(GetLoadableTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false });

        return FromTypes(providerTypes);
    }

    /// <summary>
    /// Gets the credential schema for a provider implementation type.
    /// </summary>
    public CredentialSchema? Get(Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        return _byProviderType.TryGetValue(providerType, out var schema) ? schema : null;
    }

    /// <summary>
    /// Gets the credential schema for a provider ID when the provider has a <see cref="DataSourceAttribute"/>.
    /// </summary>
    public CredentialSchema? Get(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _byProviderId.TryGetValue(providerId, out var schema) ? schema : null;
    }

    private static CredentialSchema? CreateSchema(Type providerType)
    {
        var attributes = AttributeCredentialResolver.GetAttributes(providerType);
        if (attributes.Count == 0)
        {
            return null;
        }

        var dataSource = providerType.GetDataSourceAttribute();
        return new CredentialSchema(
            providerType,
            dataSource?.Id,
            dataSource?.DisplayName,
            attributes);
    }

    private static IReadOnlyDictionary<string, CredentialSchema> BuildProviderIdIndex(IEnumerable<CredentialSchema> schemas)
    {
        var byProviderId = new Dictionary<string, CredentialSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var schema in schemas)
        {
            if (string.IsNullOrWhiteSpace(schema.ProviderId))
            {
                continue;
            }

            if (!byProviderId.TryAdd(schema.ProviderId, schema))
            {
                throw new InvalidOperationException(
                    $"Duplicate credential schema registration for provider ID '{schema.ProviderId}'.");
            }
        }

        return byProviderId;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}

/// <summary>
/// Credential schema metadata for a provider type discovered from <see cref="RequiresCredentialAttribute"/>.
/// </summary>
/// <param name="ProviderType">Provider implementation type declaring the credential attributes.</param>
/// <param name="ProviderId">Provider ID when available from <see cref="DataSourceAttribute"/>.</param>
/// <param name="DisplayName">Provider display name when available from <see cref="DataSourceAttribute"/>.</param>
/// <param name="Fields">Credential field metadata declared on the provider type.</param>
public sealed record CredentialSchema(
    Type ProviderType,
    string? ProviderId,
    string? DisplayName,
    IReadOnlyList<RequiresCredentialAttribute> Fields);
