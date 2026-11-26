using Meridian.Contracts.EntityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// ADR-033 consumer: groups per-symbol gross exposure by issuer through the entity master. A symbol is
/// resolved to its security canonically (an ambiguous ticker resolves to no security), and the security
/// to its issuer by the valid-dated issuer link as of the report date. Exposure that cannot be tied to
/// an issuer is reported as unlinked, never dropped.
/// </summary>
public sealed class IssuerConcentrationService
{
    private readonly ISecurityResolver? _resolver;
    private readonly IEntityMasterStore? _entityMaster;

    /// <param name="entityMaster">The entity master; without one no concentration is computed.</param>
    /// <param name="resolver">The canonical security resolver; without one (no Security Master database) no concentration is computed.</param>
    public IssuerConcentrationService(IEntityMasterStore? entityMaster, ISecurityResolver? resolver = null)
    {
        _entityMaster = entityMaster;
        _resolver = resolver;
    }

    /// <summary>The concentration, or null when the host has no entity master or canonical security resolver.</summary>
    public async Task<IssuerConcentrationReport?> ComputeAsync(
        IReadOnlyList<(string Symbol, decimal Gross)> symbolGross,
        decimal totalGross,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbolGross);
        if (_resolver is null || _entityMaster is null)
            return null;

        var byIssuer = new Dictionary<Guid, (decimal Gross, SortedSet<string> Symbols)>();
        var unlinkedSymbols = new SortedSet<string>(StringComparer.Ordinal);
        var unlinkedGross = 0m;
        var asOfInstant = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var (symbol, gross) in symbolGross)
        {
            ct.ThrowIfCancellationRequested();
            var issuerId = await ResolveIssuerAsync(symbol, asOf, asOfInstant, ct).ConfigureAwait(false);
            if (issuerId is not { } issuer)
            {
                unlinkedGross += gross;
                unlinkedSymbols.Add(symbol);
                continue;
            }

            if (!byIssuer.TryGetValue(issuer, out var entry))
                entry = (0m, new SortedSet<string>(StringComparer.Ordinal));
            entry.Symbols.Add(symbol);
            byIssuer[issuer] = (entry.Gross + gross, entry.Symbols);
        }

        var issuers = new List<IssuerConcentrationDto>(byIssuer.Count);
        foreach (var (issuerId, entry) in byIssuer)
        {
            var entity = await _entityMaster!.GetEntityAsync(issuerId, ct).ConfigureAwait(false);
            var ultimate = await _entityMaster!.GetUltimateParentAsync(issuerId, asOf, ct).ConfigureAwait(false);
            issuers.Add(new IssuerConcentrationDto(
                issuerId,
                entity?.LegalName ?? issuerId.ToString("D"),
                ultimate?.EntityId,
                entry.Gross,
                totalGross > 0m ? decimal.Round(entry.Gross / totalGross, 6, MidpointRounding.AwayFromZero) : 0m,
                entry.Symbols.ToArray()));
        }

        return new IssuerConcentrationReport(
            asOf,
            issuers
                .OrderByDescending(static issuer => issuer.GrossExposure)
                .ThenBy(static issuer => issuer.LegalName, StringComparer.Ordinal)
                .ToArray(),
            issuers.Sum(static issuer => issuer.GrossExposure),
            unlinkedGross,
            unlinkedSymbols.ToArray());
    }

    private async Task<Guid?> ResolveIssuerAsync(string symbol, DateOnly asOf, DateTimeOffset asOfInstant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var resolution = await _resolver!
            .ResolveCanonicalAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, symbol.Trim(), Provider: null, asOfInstant), ct)
            .ConfigureAwait(false);
        if (resolution.Status != SecurityResolutionStatus.Resolved || resolution.SecurityId is not { } securityId)
            return null;

        var link = await _entityMaster!.GetIssuerAsync(securityId, asOf, ct).ConfigureAwait(false);
        return link?.IssuerEntityId;
    }
}
