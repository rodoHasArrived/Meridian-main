using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Reporting;

/// <summary>
/// Calculates and attributes Net Asset Value across a fund structure by combining ledger balances
/// with Security Master instrument definitions.
/// </summary>
public sealed class NavAttributionService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ILogger<NavAttributionService> _log;

    public NavAttributionService(
        ISecurityMasterQueryService securityMaster,
        ILogger<NavAttributionService>? log = null)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
        _log = log ?? NullLogger<NavAttributionService>.Instance;
    }

    public async Task<NavAttributionResult> AttributeAsync(
        NavAttributionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _log.LogInformation(
            "Computing NAV attribution for fund {FundId} asOf {AsOf}",
            request.FundId, request.AsOf);

        var snapshot = request.FundLedger.ReconciliationSnapshot(request.AsOf);

        var consolidatedNav = await ComputeNavAsync(
            snapshot.Consolidated.Balances, request.AsOf, ct).ConfigureAwait(false);

        var entityNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entityId, entitySnapshot) in snapshot.Entities)
        {
            ct.ThrowIfCancellationRequested();
            entityNav[entityId] = await ComputeNavAsync(entitySnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        var sleeveNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sleeveId, sleeveSnapshot) in snapshot.Sleeves)
        {
            ct.ThrowIfCancellationRequested();
            sleeveNav[sleeveId] = await ComputeNavAsync(sleeveSnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        var vehicleNav = new Dictionary<string, NavBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vehicleId, vehicleSnapshot) in snapshot.Vehicles)
        {
            ct.ThrowIfCancellationRequested();
            vehicleNav[vehicleId] = await ComputeNavAsync(vehicleSnapshot.Balances, request.AsOf, ct)
                .ConfigureAwait(false);
        }

        _log.LogInformation(
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
                    _log.LogDebug(ex, "Security Master lookup failed for symbol {Symbol}", account.Symbol);
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

        // NAV is assets net of liabilities. Equity, revenue, and expense balances are the
        // financing/earnings view of the same value (Assets − Liabilities = Equity + Net Income),
        // so summing them alongside the assets would double-count the fund's value.
        var totalNav = 0m;
        var byAssetClass = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in components)
        {
            var signedContribution = component.AccountType switch
            {
                nameof(LedgerAccountType.Asset) => component.Balance,
                nameof(LedgerAccountType.Liability) => -component.Balance,
                _ => 0m
            };
            if (signedContribution == 0m)
            {
                continue;
            }

            totalNav += signedContribution;
            var assetClass = component.AssetClass ?? "Unclassified";
            byAssetClass[assetClass] = byAssetClass.GetValueOrDefault(assetClass) + signedContribution;
        }

        return new NavBreakdown(totalNav, components, byAssetClass);
    }
}

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
