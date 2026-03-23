using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Resolves workstation-friendly instrument metadata for portfolio and ledger drill-ins.
/// </summary>
public interface ISecurityReferenceLookup
{
    Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default);
}
