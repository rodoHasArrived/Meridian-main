using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Coverage source exposing every canonical symbol the canonicalization spine knows about.
/// These symbols entered the registry through real usage (streaming enrichment, resolver
/// learning, seeding), so any of them without a validated security-master record is a live
/// coverage gap. Returns an empty set when no registry is available.
/// </summary>
public sealed class CanonicalRegistryCoverageSource : ISecurityCoverageSymbolSource
{
    private readonly ICanonicalSymbolRegistry? _registry;

    public CanonicalRegistryCoverageSource(ICanonicalSymbolRegistry? registry = null)
    {
        _registry = registry;
    }

    public string SourceName => "canonical-symbol-registry";

    public Task<IReadOnlyCollection<string>> GetActiveSymbolsAsync(CancellationToken ct = default)
    {
        if (_registry is null)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        var symbols = _registry.GetAll()
            .Select(static definition => definition.Canonical)
            .Where(static canonical => !string.IsNullOrWhiteSpace(canonical))
            .Select(static canonical => canonical.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<string>>(symbols);
    }
}
