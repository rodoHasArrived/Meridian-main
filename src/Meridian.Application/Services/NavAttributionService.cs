using Meridian.Application.Logging;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Calculates and attributes Net Asset Value (NAV) across a fund structure
/// by combining ledger balances with Security Master instrument definitions.
/// Supports entity-, sleeve-, and vehicle-level attribution using
/// <see cref="FundLedgerBook"/>.
/// </summary>
public sealed class NavAttributionService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ILogger _log = LoggingSetup.ForContext<NavAttributionService>();

    public NavAttributionService(ISecurityMasterQueryService securityMaster)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes NAV attribution for the requested fund at the given point-in-time.
    /// Returns consolidated NAV as well as per-entity, per-sleeve, and per-vehicle breakdowns.
    /// </summary>
    public async Task<NavAttributionResult> AttributeAsync(
        NavAttributionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _log.Information(
            "Computing NAV attribution for fund {FundId} asOf {AsOf}",
            request.FundId, request.AsOf);

        var snapshot = request.FundLedger.ReconciliationSnapshot(request.AsOf);

        // Consolidated NAV
        var consolidatedNav = await ComputeNavAsync(
            snapshot.Consolidated.Balances, request.AsOf, ct).ConfigureAwait(false);

        // Per-entity attribution
        var entityNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entityId, entitySnapshot) in snapshot.Entities)
        {
            ct.ThrowIfCancellationRequested();
            entityNav[entityId] = await ComputeNavAsync(entitySnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        // Per-sleeve attribution
        var sleeveNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sleeveId, sleeveSnapshot) in snapshot.Sleeves)
        {
            ct.ThrowIfCancellationRequested();
            sleeveNav[sleeveId] = await ComputeNavAsync(sleeveSnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        // Per-vehicle attribution
        var vehicleNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vehicleId, vehicleSnapshot) in snapshot.Vehicles)
        {
            ct.ThrowIfCancellationRequested();
            vehicleNav[vehicleId] = await ComputeNavAsync(vehicleSnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        _log.Information(
            "NAV attribution complete for fund {FundId}: consolidated NAV = {Nav} {Currency}",
            request.FundId, consolidatedNav.TotalNav, request.Currency);

        return new NavAttributionResult(
            FundId: request.FundId,
            AsOf: request.AsOf,
            Currency: request.Currency,
            ComputedAt: DateTimeOffset.UtcNow,
            Consolidated: consolidatedNav,
            ByEntity: entityNav,
            BySleeve: sleeveNav,
            ByVehicle: vehicleNav);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<NavBreakdown> ComputeNavAsync(
        IReadOnlyDictionary<LedgerAccount, decimal> balances,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var components = new List<NavComponent>();

        foreach (var (account, balance) in balances)
        {
            ct.ThrowIfCancellationRequested();

            SecurityDetailDto? security = null;
            if (!string.IsNullOrWhiteSpace(account.Symbol))
            {
                try
                {
                    security = await _securityMaster
                        .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, account.Symbol, null, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Security Master lookup failed for symbol {Symbol}", account.Symbol);
                }
            }

            components.Add(new NavComponent(
                AccountName: account.Name,
                AccountType: account.AccountType.ToString(),
                Symbol: account.Symbol,
                AssetClass: security?.AssetClass,
                DisplayName: security?.DisplayName,
                Balance: balance));
        }

        var totalNav = components.Sum(c => c.Balance);
        var byAssetClass = components
            .GroupBy(c => c.AssetClass ?? "Unclassified", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Balance));

        return new NavBreakdown(totalNav, components, byAssetClass);
    }
}

// ── Request / result models ────────────────────────────────────────────────────

/// <summary>Request payload for <see cref="NavAttributionService.AttributeAsync"/>.</summary>
public sealed record NavAttributionRequest(
    string FundId,
    DateTimeOffset AsOf,
    FundLedgerBook FundLedger,
    string Currency = "USD");

/// <summary>NAV contribution from a single ledger account.</summary>
public sealed record NavComponent(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? AssetClass,
    string? DisplayName,
    decimal Balance);

/// <summary>NAV summary for one dimension (consolidated, entity, sleeve, or vehicle).</summary>
public sealed record NavBreakdown(
    decimal TotalNav,
    IReadOnlyList<NavComponent> Components,
    IReadOnlyDictionary<string, decimal> ByAssetClass);

/// <summary>Full NAV attribution result across all fund dimensions.</summary>
public sealed record NavAttributionResult(
    string FundId,
    DateTimeOffset AsOf,
    string Currency,
    DateTimeOffset ComputedAt,
    NavBreakdown Consolidated,
    IReadOnlyDictionary<string, NavBreakdown> ByEntity,
    IReadOnlyDictionary<string, NavBreakdown> BySleeve,
    IReadOnlyDictionary<string, NavBreakdown> ByVehicle);
