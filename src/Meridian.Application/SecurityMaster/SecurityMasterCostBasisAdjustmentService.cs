using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Reads day-count, factor, and corporate-action reference data from the Security Master and
/// projects it into <see cref="LedgerTaxLotBasisAdjustment"/>s that the ledger relief engine
/// consumes. This is the linkage between tax-lot / cost-basis accounting and Security Master
/// reference data: cost basis is relieved against factor- and corporate-action-restated,
/// day-count-amortized lots instead of their raw recorded quantity and unit cost.
/// </summary>
public interface ISecurityMasterCostBasisAdjustmentService
{
    /// <summary>
    /// Builds the reference-data basis adjustments for <paramref name="securityId"/> effective as
    /// of <paramref name="asOf"/>: forward/reverse splits and return-of-capital from corporate
    /// actions, current pool-factor paydown, and per-lot straight-line premium/discount
    /// amortization derived from the master's day-count convention.
    /// </summary>
    /// <param name="priorFactor">
    /// The pool factor already reflected in the lots' recorded face (default <c>1</c> — lots
    /// recorded at original face). A current factor below this produces a paydown adjustment.
    /// </param>
    Task<IReadOnlyList<LedgerTaxLotBasisAdjustment>> BuildAdjustmentsAsync(
        Guid securityId,
        IReadOnlyList<LedgerTaxLot> openLots,
        DateOnly asOf,
        decimal priorFactor = 1m,
        CancellationToken ct = default);

    /// <summary>
    /// Enriches <paramref name="baseInput"/> with the Security Master basis adjustments for
    /// <paramref name="securityId"/> and projects realized-gain relief. Equivalent to calling
    /// <see cref="BuildAdjustmentsAsync"/> and re-running
    /// <see cref="LedgerTaxLotReliefProjector.Project"/> with the adjustments applied.
    /// </summary>
    Task<LedgerTaxLotReliefProjection> ProjectReliefAsync(
        LedgerTaxLotReliefInput baseInput,
        Guid securityId,
        decimal priorFactor = 1m,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SecurityMasterCostBasisAdjustmentService : ISecurityMasterCostBasisAdjustmentService
{
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterCostBasisAdjustmentService> _logger;

    public SecurityMasterCostBasisAdjustmentService(
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCostBasisAdjustmentService> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTaxLotBasisAdjustment>> BuildAdjustmentsAsync(
        Guid securityId,
        IReadOnlyList<LedgerTaxLot> openLots,
        DateOnly asOf,
        decimal priorFactor = 1m,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openLots);

        var adjustments = new List<LedgerTaxLotBasisAdjustment>();
        AddCorporateActionAdjustments(adjustments, await GetEffectiveActionsAsync(securityId, asOf, ct).ConfigureAwait(false), securityId, asOf);

        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is not null)
        {
            AddFactorAdjustment(adjustments, security, securityId, asOf, priorFactor);
            AddAmortizationAdjustments(adjustments, security, openLots, securityId, asOf);
        }

        return adjustments;
    }

    /// <inheritdoc />
    public async Task<LedgerTaxLotReliefProjection> ProjectReliefAsync(
        LedgerTaxLotReliefInput baseInput,
        Guid securityId,
        decimal priorFactor = 1m,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseInput);

        var adjustments = await BuildAdjustmentsAsync(securityId, baseInput.OpenLots, baseInput.SaleDate, priorFactor, ct)
            .ConfigureAwait(false);

        // Preserve any adjustments already carried on the base input (explicit caller overrides)
        // ahead of the ones sourced from the Security Master.
        var combined = baseInput.BasisAdjustments.Concat(adjustments).ToArray();

        var enrichedInput = new LedgerTaxLotReliefInput(
            baseInput.Account,
            baseInput.SaleDate,
            baseInput.QuantitySold,
            baseInput.SalePrice,
            baseInput.ReliefMethod,
            baseInput.OpenLots,
            baseInput.FinancialAccountId,
            baseInput.SpecificLotIds,
            combined);

        return LedgerTaxLotReliefProjector.Project(enrichedInput);
    }

    private async Task<IReadOnlyList<CorporateActionDto>> GetEffectiveActionsAsync(
        Guid securityId, DateOnly asOf, CancellationToken ct)
    {
        var actions = await _queryService.GetCorporateActionsAsync(securityId, ct).ConfigureAwait(false);
        if (actions is null || actions.Count == 0)
            return [];

        return CorporateActionEffectiveStateProjector.ProjectEffectiveActions(actions, EndOfDay(asOf));
    }

    private void AddCorporateActionAdjustments(
        List<LedgerTaxLotBasisAdjustment> adjustments,
        IReadOnlyList<CorporateActionDto> effectiveActions,
        Guid securityId,
        DateOnly asOf)
    {
        foreach (var action in effectiveActions)
        {
            if (action.ExDate > asOf)
                continue;

            var canonical = CorporateActionEventTypes.Normalize(action.EventType);
            var reference = $"corp-action:{action.CorpActId:D}";

            if (canonical is CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit
                && action.SplitRatio is { } ratio && ratio > 0m)
            {
                adjustments.Add(new LedgerTaxLotBasisAdjustment(
                    LedgerTaxLotBasisAdjustmentKind.Split, ratio, action.ExDate, securityId, LotId: null, reference).EnsureValid());
            }
            else if (canonical == CorporateActionEventTypes.ReturnOfCapital
                && action.DividendPerShare is { } perShare && perShare > 0m)
            {
                adjustments.Add(new LedgerTaxLotBasisAdjustment(
                    LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital, perShare, action.ExDate, securityId, LotId: null, reference).EnsureValid());
            }
        }
    }

    private void AddFactorAdjustment(
        List<LedgerTaxLotBasisAdjustment> adjustments,
        SecurityDetailDto security,
        Guid securityId,
        DateOnly asOf,
        decimal priorFactor)
    {
        if (priorFactor <= 0m)
        {
            _logger.LogDebug("Skipping factor adjustment for {SecurityId}: prior factor {PriorFactor} is not positive.", securityId, priorFactor);
            return;
        }

        if (!TryReadDecimal(security, out var currentFactor, "currentFactor", "factor") || currentFactor is not > 0m)
            return;

        var ratio = currentFactor.Value / priorFactor;
        if (ratio >= 1m)
            return; // No paydown (factor unchanged or increased): nothing to relieve from basis.

        adjustments.Add(new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Factor, ratio, asOf, securityId, LotId: null, "current-factor").EnsureValid());
    }

    private void AddAmortizationAdjustments(
        List<LedgerTaxLotBasisAdjustment> adjustments,
        SecurityDetailDto security,
        IReadOnlyList<LedgerTaxLot> openLots,
        Guid securityId,
        DateOnly asOf)
    {
        if (!IsFixedIncome(security.AssetClass))
            return;
        if (!TryReadDate(security, out var maturity, "maturityDate", "maturity", "legalFinalMaturity"))
            return;

        _ = TryReadString(security, out var dayCount, "dayCountConvention", "dayCount", "dayCountBasis");

        // Fixed-income lots are recorded as a price per 100 of par (bond quotation convention);
        // premium/discount amortizes straight-line toward par, weighted by the day-count year
        // fraction from acquisition to the sale/as-of date.
        const decimal parPrice = 100m;
        var amortizeTo = asOf < maturity ? asOf : maturity;

        foreach (var lot in openLots)
        {
            if (lot.SecurityId is { } lotSecurityId && lotSecurityId != securityId)
                continue;
            if (lot.AcquiredDate >= amortizeTo || lot.AcquiredDate >= maturity)
                continue;

            var premiumOrDiscount = lot.UnitCost - parPrice;
            if (premiumOrDiscount == 0m)
                continue;

            var elapsed = YearFraction(dayCount, lot.AcquiredDate, amortizeTo);
            var life = YearFraction(dayCount, lot.AcquiredDate, maturity);
            if (life <= 0m)
                continue;

            var fraction = elapsed / life;
            if (fraction <= 0m)
                continue;
            if (fraction > 1m)
                fraction = 1m;

            // Signed per-unit delta that moves basis toward par: premium (unit cost > par) yields a
            // negative delta (write-down); discount (unit cost < par) yields a positive delta.
            var delta = decimal.Round((parPrice - lot.UnitCost) * fraction, 10, MidpointRounding.AwayFromZero);
            if (delta == 0m)
                continue;

            adjustments.Add(new LedgerTaxLotBasisAdjustment(
                LedgerTaxLotBasisAdjustmentKind.Amortization, delta, asOf, securityId, lot.LotId, "straight-line-amortization").EnsureValid());
        }
    }

    private static bool IsFixedIncome(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
            return false;

        return assetClass.Trim().ToUpperInvariant() switch
        {
            "BOND" or "TREASURYBILL" or "CERTIFICATEOFDEPOSIT" or "COMMERCIALPAPER"
                or "MORTGAGEBACKED" or "MORTGAGEBACKEDSECURITY" or "MBS"
                or "ASSETBACKED" or "ASSETBACKEDSECURITY" or "ABS"
                or "STRUCTUREDCREDIT" or "LOAN" or "DIRECTLOAN" or "AMORTIZINGLOAN" => true,
            _ => false,
        };
    }

    private static DateTimeOffset EndOfDay(DateOnly date)
        => new(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

    // ── Day-count year fraction (mirrors SecurityMasterCashFlowService tokens) ─────────────────

    private static decimal YearFraction(string? dayCountConvention, DateOnly start, DateOnly end)
    {
        if (end <= start)
            return 0m;

        var normalized = dayCountConvention?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized is not null && normalized.Contains("30/360", StringComparison.Ordinal))
            return Days360(start, end) / 360m;

        var actualDays = end.DayNumber - start.DayNumber;
        if (normalized is not null && (normalized.Contains("ACT/360", StringComparison.Ordinal) || normalized.Contains("ACTUAL/360", StringComparison.Ordinal)))
            return actualDays / 360m;

        // Default (including ACT/365 and unrecognized conventions): actual days over 365.
        return actualDays / 365m;
    }

    private static decimal Days360(DateOnly start, DateOnly end)
    {
        var startDay = Math.Min(start.Day, 30);
        var endDay = end.Day == 31 && startDay == 30 ? 30 : Math.Min(end.Day, 30);
        return ((end.Year - start.Year) * 360m) + ((end.Month - start.Month) * 30m) + (endDay - startDay);
    }

    // ── Security-term JSON readers (mirror SecurityMasterCashFlowService) ──────────────────────

    private static bool TryReadString(SecurityDetailDto security, out string? value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                    continue;

                if (property.ValueKind == JsonValueKind.String)
                {
                    value = property.GetString();
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadDecimal(SecurityDetailDto security, out decimal? value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                    continue;

                if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
                {
                    value = number;
                    return true;
                }

                if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadDate(SecurityDetailDto security, out DateOnly value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                    continue;

                var raw = property.GetString();
                if (DateOnly.TryParse(raw, out value))
                    return true;

                if (DateTimeOffset.TryParse(raw, out var timestamp))
                {
                    value = DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(SecurityDetailDto security)
    {
        yield return security.AssetSpecificTerms;
        if (TryGetProperty(security.AssetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return security.CommonTerms;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
