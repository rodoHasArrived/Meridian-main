using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Application.Accounting;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>One explicit durable run/account position-snapshot scope.</summary>
public sealed record DailyValuationPositionSnapshotScope(string RunId, string AccountId);

/// <summary>Fail-closed result of resolving the positions for one valuation run.</summary>
public sealed record DailyValuationPositionResolution(
    IReadOnlyList<MarkToMarketPosition> Positions,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Blockers.Count == 0 && Positions.Count > 0;
}

/// <summary>
/// Resolves current positions from explicitly named durable snapshots, or from an explicitly
/// time-stamped static override. Every position must resolve to an active Security Master record
/// in the valuation base currency before a provider mark can enter an accounting draft.
/// </summary>
public sealed class DailyValuationPositionService
{
    private readonly IPositionSnapshotStore? _snapshotStore;
    private readonly ICanonicalSymbolRegistry? _symbolRegistry;
    private readonly ISecurityMasterQueryService? _securityMaster;

    public DailyValuationPositionService(
        IPositionSnapshotStore? snapshotStore,
        ICanonicalSymbolRegistry? symbolRegistry,
        ISecurityMasterQueryService? securityMaster)
    {
        _snapshotStore = snapshotStore;
        _symbolRegistry = symbolRegistry;
        _securityMaster = securityMaster;
    }

    public async Task<DailyValuationPositionResolution> ResolveConfiguredAsync(
        DailyValuationScheduleWorkItem workItem,
        DateTimeOffset valuationAsOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        valuationAsOfUtc = valuationAsOfUtc.ToUniversalTime();

        if (workItem.PositionSnapshotScopes.Count > 0 && workItem.UseStaticPositionOverride)
        {
            return Blocked("Daily valuation cannot combine durable position snapshots with a static position override.");
        }

        if (workItem.PositionSnapshotScopes.Count > 0)
        {
            if (_snapshotStore is null)
            {
                return Blocked("Durable position snapshots are configured, but the position snapshot store is unavailable.");
            }

            var positions = new List<MarkToMarketPosition>();
            var evidence = new List<OperationsEvidenceLinkDto>();
            var blockers = new List<string>();
            foreach (var scope in workItem.PositionSnapshotScopes)
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = await _snapshotStore
                    .GetLatestSnapshotAsync(scope.RunId, scope.AccountId, ct)
                    .ConfigureAwait(false);
                if (snapshot is null)
                {
                    blockers.Add($"No durable position snapshot exists for run '{scope.RunId}' and account '{scope.AccountId}'.");
                    continue;
                }

                var freshnessBlocker = ValidateFreshness(
                    snapshot.AsOf,
                    valuationAsOfUtc,
                    workItem.MaximumPositionAgeDays,
                    $"Position snapshot '{scope.RunId}/{scope.AccountId}'");
                if (freshnessBlocker is not null)
                {
                    blockers.Add(freshnessBlocker);
                    continue;
                }

                evidence.Add(new OperationsEvidenceLinkDto(
                    $"daily-valuation-position:{workItem.ScheduleId}:{scope.RunId}:{scope.AccountId}",
                    "Durable portfolio position snapshot",
                    BuildSnapshotEvidenceRoute(scope, snapshot.AsOf),
                    "position-snapshot-store",
                    snapshot.AsOf));
                positions.AddRange(snapshot.Positions
                    .Where(static position => position.Quantity != 0m)
                    .Select(position => new MarkToMarketPosition(
                        position.Symbol,
                        position.Quantity,
                        position.CostBasis,
                        scope.AccountId)));
            }

            if (blockers.Count > 0)
            {
                return new DailyValuationPositionResolution([], evidence, blockers);
            }

            return await ResolveSecurityMasterAsync(positions, workItem.Currency, valuationAsOfUtc, evidence, ct)
                .ConfigureAwait(false);
        }

        if (!workItem.UseStaticPositionOverride)
        {
            return Blocked(
                "No fresh durable position-snapshot scope is configured. Static positions require an explicit, time-stamped override.");
        }

        if (!workItem.StaticPositionsAsOfUtc.HasValue)
        {
            return Blocked("The static position override is missing its as-of timestamp.");
        }

        var overrideFreshnessBlocker = ValidateFreshness(
            workItem.StaticPositionsAsOfUtc.Value,
            valuationAsOfUtc,
            workItem.MaximumPositionAgeDays,
            "Static position override");
        if (overrideFreshnessBlocker is not null)
        {
            return Blocked(overrideFreshnessBlocker);
        }

        if (workItem.Positions.Count == 0)
        {
            return Blocked("The explicit static position override contains no open positions.");
        }

        var actualHash = ComputeStaticPositionHash(workItem.Positions);
        if (string.IsNullOrWhiteSpace(workItem.StaticPositionHash) ||
            !string.Equals(actualHash, workItem.StaticPositionHash, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("The static position override hash does not match the retained configured positions.");
        }

        var staticEvidence = new OperationsEvidenceLinkDto(
            $"daily-valuation-position:{workItem.ScheduleId}:static-override",
            "Explicit static position override",
            $"evidence://daily-valuation/position-override/{Uri.EscapeDataString(workItem.ScheduleId)}/{actualHash}",
            "daily-valuation-scheduler",
            workItem.StaticPositionsAsOfUtc.Value.ToUniversalTime());
        return await ResolveSecurityMasterAsync(
                workItem.Positions,
                workItem.Currency,
                valuationAsOfUtc,
                [staticEvidence],
                ct)
            .ConfigureAwait(false);
    }

    public Task<DailyValuationPositionResolution> ResolveAdHocAsync(
        IReadOnlyList<MarkToMarketPosition> positions,
        string baseCurrency,
        DateTimeOffset valuationAsOfUtc,
        CancellationToken ct = default)
        => ResolveSecurityMasterAsync(positions, baseCurrency, valuationAsOfUtc.ToUniversalTime(), [], ct);

    public static string ComputeStaticPositionHash(IReadOnlyList<MarkToMarketPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var canonical = positions
            .Select(static position => string.Join(
                '|',
                position.SecurityId?.ToString("N") ?? "-",
                position.Symbol?.Trim().ToUpperInvariant() ?? string.Empty,
                position.FinancialAccountId?.Trim() ?? "-",
                position.InstrumentType?.Trim().ToUpperInvariant() ?? "-",
                position.Quantity.ToString(CultureInfo.InvariantCulture),
                position.CostPrice.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', canonical))))
            .ToLowerInvariant();
    }

    private async Task<DailyValuationPositionResolution> ResolveSecurityMasterAsync(
        IReadOnlyList<MarkToMarketPosition> positions,
        string baseCurrency,
        DateTimeOffset valuationAsOfUtc,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        CancellationToken ct)
    {
        if (positions.Count == 0)
        {
            return new DailyValuationPositionResolution([], evidence, ["No open positions are available for daily valuation."]);
        }

        if (_symbolRegistry is null || _securityMaster is null)
        {
            return new DailyValuationPositionResolution(
                [],
                evidence,
                ["Daily valuation requires the canonical symbol registry and authoritative Security Master query service."]);
        }

        var normalizedCurrency = RequireText(baseCurrency, nameof(baseCurrency)).ToUpperInvariant();
        var resolved = new List<MarkToMarketPosition>(positions.Count);
        var blockers = new List<string>();
        foreach (var position in positions)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = position.Symbol?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                blockers.Add("A configured position is missing its canonical symbol.");
                continue;
            }

            var definition = _symbolRegistry.GetDefinition(symbol);
            if (definition?.SecurityId is not { } securityId || securityId == Guid.Empty)
            {
                blockers.Add($"Position symbol '{symbol}' is unresolved or ambiguous in the canonical symbol registry.");
                continue;
            }

            if (position.SecurityId.HasValue && position.SecurityId.Value != securityId)
            {
                blockers.Add($"Position symbol '{symbol}' supplied Security Master id '{position.SecurityId:D}', but the canonical registry resolved '{securityId:D}'.");
                continue;
            }

            var security = await _securityMaster.GetByIdAsOfAsync(securityId, valuationAsOfUtc, ct).ConfigureAwait(false)
                           ?? await _securityMaster.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (security is null)
            {
                blockers.Add($"Position symbol '{symbol}' resolved to Security Master id '{securityId:D}', but no authoritative record exists.");
                continue;
            }

            if (security.Status != SecurityStatusDto.Active)
            {
                blockers.Add($"Position symbol '{symbol}' resolves to {security.Status} Security Master record '{securityId:D}'.");
                continue;
            }

            if (!SecurityContainsSymbol(security, definition.Canonical, symbol))
            {
                blockers.Add($"Position symbol '{symbol}' does not match authoritative Security Master record '{securityId:D}'.");
                continue;
            }

            if (!string.Equals(security.Currency?.Trim(), normalizedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Position symbol '{symbol}' is denominated in '{security.Currency}', not valuation base currency '{normalizedCurrency}'; configure governed FX translation before posting.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(definition.Currency) &&
                !string.Equals(definition.Currency.Trim(), normalizedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Canonical symbol '{definition.Canonical}' is denominated in '{definition.Currency}', not valuation base currency '{normalizedCurrency}'.");
                continue;
            }

            resolved.Add(position with
            {
                Symbol = definition.Canonical.Trim().ToUpperInvariant(),
                InstrumentType = security.AssetClass,
                SecurityId = securityId
            });
        }

        var duplicate = resolved
            .GroupBy(static position => MarkToMarketCarryingValueKey.FromPosition(position))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            blockers.Add($"Position sources contain duplicate security/account scope '{duplicate.Key.Symbol}/{duplicate.Key.FinancialAccountId ?? "unscoped"}'; reconcile the scopes before valuation.");
        }

        return blockers.Count > 0
            ? new DailyValuationPositionResolution([], evidence, blockers)
            : new DailyValuationPositionResolution(resolved, evidence, []);
    }

    private static string? ValidateFreshness(
        DateTimeOffset sourceAsOfUtc,
        DateTimeOffset valuationAsOfUtc,
        int maximumAgeDays,
        string label)
    {
        sourceAsOfUtc = sourceAsOfUtc.ToUniversalTime();
        if (sourceAsOfUtc > valuationAsOfUtc)
        {
            return $"{label} is dated after the valuation timestamp and cannot be used as-of {valuationAsOfUtc:O}.";
        }

        if (sourceAsOfUtc < valuationAsOfUtc.AddDays(-maximumAgeDays))
        {
            return $"{label} is stale as-of {sourceAsOfUtc:O}; maximum position age is {maximumAgeDays} day(s).";
        }

        return null;
    }

    private static string BuildSnapshotEvidenceRoute(
        DailyValuationPositionSnapshotScope scope,
        DateTimeOffset snapshotAsOfUtc)
        => $"evidence://position-snapshots/{Uri.EscapeDataString(scope.RunId)}/{Uri.EscapeDataString(scope.AccountId)}?asOf={Uri.EscapeDataString(snapshotAsOfUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";

    private static bool SecurityContainsSymbol(SecurityDetailDto security, params string[] candidates)
    {
        var tokens = candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(static candidate => candidate.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tokens.Contains(security.DisplayName) ||
               security.Identifiers.Any(identifier =>
                   tokens.Contains(identifier.Value) ||
                   (!string.IsNullOrWhiteSpace(identifier.NormalizedValue) && tokens.Contains(identifier.NormalizedValue))) ||
               security.Aliases.Any(alias => alias.IsEnabled && tokens.Contains(alias.AliasValue));
    }

    private static DailyValuationPositionResolution Blocked(string blocker)
        => new([], [], [blocker]);

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}
