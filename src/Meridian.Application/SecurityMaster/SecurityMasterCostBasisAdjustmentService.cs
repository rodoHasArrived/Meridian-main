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
    /// actions, pool-factor paydown (the dated factor schedule as of <paramref name="asOf"/>,
    /// falling back to the scalar current factor), and per-lot straight-line premium/discount
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
            // One typed term resolve shared with the cash-flow projection path, so factor and
            // day-count reads cannot diverge from the terms the projections themselves use.
            var terms = StructuredCashFlowTermsResolver.Resolve(security);
            AddFactorAdjustment(adjustments, terms, securityId, asOf, priorFactor);
            AddAmortizationAdjustments(adjustments, security, terms, openLots, securityId, asOf);
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
        StructuredCashFlowTerms terms,
        Guid securityId,
        DateOnly asOf,
        decimal priorFactor)
    {
        if (priorFactor <= 0m)
        {
            _logger.LogDebug("Skipping factor adjustment for {SecurityId}: prior factor {PriorFactor} is not positive.", securityId, priorFactor);
            return;
        }

        // The dated factor schedule (factor in effect on asOf) takes priority over the scalar
        // currentFactor — the same sourcing rule the cash-flow projector applies — so cost-basis
        // relief and projected paydowns read one factor for the same date.
        var currentFactor = terms.FactorAsOf(asOf);
        if (currentFactor is not > 0m)
            return;

        var ratio = currentFactor.Value / priorFactor;
        if (ratio >= 1m)
            return; // No paydown (factor unchanged or increased): nothing to relieve from basis.

        var reference = terms.FactorSchedule.Any(entry => entry.AsOfDate <= asOf)
            ? "factor-schedule"
            : "current-factor";
        adjustments.Add(new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Factor, ratio, asOf, securityId, LotId: null, reference).EnsureValid());
    }

    private void AddAmortizationAdjustments(
        List<LedgerTaxLotBasisAdjustment> adjustments,
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        IReadOnlyList<LedgerTaxLot> openLots,
        Guid securityId,
        DateOnly asOf)
    {
        // Registry-driven: amortization applies exactly to the classes the shared catalog marks as
        // par-priced (canonical names and vendor aliases like "MBS" both resolve there).
        if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).AmortizesTowardPar)
            return;
        if (terms.MaturityDate is not DateOnly maturity)
            return;

        var convention = DayCountConventions.Parse(terms.DayCountConvention);

        // Fixed-income lots are recorded as a price per 100 of par (bond quotation convention);
        // premium/discount amortizes straight-line toward par, weighted by the day-count year
        // fraction from acquisition to the sale/as-of date.
        const decimal parPrice = 100m;
        var amortizeTo = asOf < maturity ? asOf : maturity;

        foreach (var lot in openLots)
        {
            // Reference-data amortization only applies to lots explicitly linked to this security;
            // unlinked lots (no SecurityId) relieve at their recorded basis.
            if (lot.SecurityId != securityId)
                continue;
            if (lot.AcquiredDate >= amortizeTo || lot.AcquiredDate >= maturity)
                continue;

            var premiumOrDiscount = lot.UnitCost - parPrice;
            if (premiumOrDiscount == 0m)
                continue;

            var elapsed = DayCountConventions.Fraction(convention, lot.AcquiredDate, amortizeTo);
            var life = DayCountConventions.Fraction(convention, lot.AcquiredDate, maturity);
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

    private static DateTimeOffset EndOfDay(DateOnly date)
        => new(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
}
