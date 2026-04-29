using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Canonical security identity context passed to workstation lookup services.
/// </summary>
public sealed record SecurityReferenceLookupRequest(
    Guid? SecurityId = null,
    string? IdentifierKind = null,
    string? IdentifierValue = null,
    string? Symbol = null,
    string? Venue = null,
    string? Currency = null,
    string? AssetClass = null,
    string? Source = null);

/// <summary>
/// Resolves workstation-friendly instrument metadata for portfolio and ledger drill-ins.
/// </summary>
public interface ISecurityReferenceLookup
{
    /// <summary>
    /// Attempts to resolve security metadata using the richest canonical identity available.
    /// </summary>
    Task<WorkstationSecurityReference?> GetByCanonicalAsync(
        SecurityReferenceLookupRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbol = string.IsNullOrWhiteSpace(request.Symbol)
            ? request.IdentifierValue
            : request.Symbol;

        return string.IsNullOrWhiteSpace(symbol)
            ? Task.FromResult<WorkstationSecurityReference?>(null)
            : GetBySymbolAsync(symbol, ct);
    }

    Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default);
}
