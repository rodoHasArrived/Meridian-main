namespace Meridian.Contracts.Api;

/// <summary>
/// Provider metadata owned by one application service provider.
/// </summary>
public interface IProviderCatalog
{
    /// <summary>Identifies the metadata source exposed by catalog responses.</summary>
    string Source { get; }

    /// <summary>Gets all providers in this catalog.</summary>
    IReadOnlyList<ProviderCatalogEntry> GetAll();

    /// <summary>Gets a provider by its identifier.</summary>
    ProviderCatalogEntry? Get(string providerId);

    /// <summary>Gets providers of a type, including hybrid providers.</summary>
    IReadOnlyList<ProviderCatalogEntry> GetByType(ProviderTypeKind type);
}

/// <summary>
/// Adapts host-owned provider metadata without reading or publishing process-wide callbacks.
/// Empty runtime catalogs and unknown identifiers remain empty instead of consulting another host.
/// </summary>
public sealed class RuntimeProviderCatalog : IProviderCatalog
{
    private readonly Func<IReadOnlyList<ProviderCatalogEntry>> _getCatalog;
    private readonly Func<string, ProviderCatalogEntry?> _getEntry;

    /// <summary>Creates a catalog containing only the built-in metadata.</summary>
    public RuntimeProviderCatalog()
        : this(ProviderCatalog.GetStaticEntries, ProviderCatalog.GetStaticEntry, "static")
    {
    }

    /// <summary>Creates a catalog backed by one host's registry or retained metadata.</summary>
    public RuntimeProviderCatalog(
        Func<IReadOnlyList<ProviderCatalogEntry>> getCatalog,
        Func<string, ProviderCatalogEntry?> getEntry,
        string source = "registry")
    {
        _getCatalog = getCatalog ?? throw new ArgumentNullException(nameof(getCatalog));
        _getEntry = getEntry ?? throw new ArgumentNullException(nameof(getEntry));
        Source = source;
    }

    /// <inheritdoc />
    public string Source { get; }

    /// <inheritdoc />
    public IReadOnlyList<ProviderCatalogEntry> GetAll() => _getCatalog();

    /// <inheritdoc />
    public ProviderCatalogEntry? Get(string providerId) => _getEntry(providerId);

    /// <inheritdoc />
    public IReadOnlyList<ProviderCatalogEntry> GetByType(ProviderTypeKind type) =>
        GetAll().Where(entry => entry.ProviderType == type || entry.ProviderType == ProviderTypeKind.Hybrid).ToArray();
}
