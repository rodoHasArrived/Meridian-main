using System.Collections.Concurrent;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Events;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Execution.Services;

/// <summary>
/// Mutable position state used internally by <see cref="PaperTradingPortfolio"/>.
/// </summary>
internal sealed class PaperPosition(string symbol, decimal marketPrice = 0m)
{
    private readonly List<PositionLot> _lots = [];
    private readonly Dictionary<string, decimal> _ownerQuantities = new(StringComparer.OrdinalIgnoreCase);
    public string Symbol { get; } = symbol;
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }

    /// <summary>
    /// Realised P&amp;L booked against this symbol while the entry has been held. The account
    /// total stays on <c>AccountState.RealisedPnl</c>; this is the per-symbol share of it, and it
    /// is lost when a flat entry is dropped or restored from canonical lots (which start at zero).
    /// </summary>
    public decimal RealisedPnl { get; set; }
    public decimal MarketPrice { get; set; } = marketPrice;
    public DateTimeOffset? MarkObservedAtUtc { get; set; }
    public string? MarkSource { get; set; }
    /// <summary>Conservative source history for acquisitions still represented by this position.</summary>
    public IReadOnlyList<Meridian.Contracts.Operations.MarketDataInputEvidence>? OpeningMarketDataEvidence { get; set; }
    public Guid? AccountingSecurityId { get; private set; }
    public string? AccountingRunId { get; private set; }
    public bool AccountingChronologyKnown { get; private set; }
    private readonly HashSet<Guid> _accountingFillIds = [];
    public IReadOnlyCollection<Guid> AccountingFillIds => _accountingFillIds;

    /// <summary>Preserves unknown or mixed attribution until the old exposure is genuinely closed.</summary>
    public void RetainAccountingIdentity(Guid? securityId, string? runId, Guid fillId,
        DateTimeOffset occurredAt, bool opensNewHistory)
    {
        securityId = securityId is { } id && id != Guid.Empty ? id : null;
        runId = string.IsNullOrWhiteSpace(runId) ? null : runId.Trim();
        if (opensNewHistory)
        {
            AccountingSecurityId = securityId;
            AccountingRunId = runId;
            AccountingChronologyKnown = occurredAt != default;
            _accountingFillIds.Clear();
        }
        else
        {
            if (AccountingSecurityId != securityId)
                AccountingSecurityId = null;
            if (!string.Equals(AccountingRunId, runId, StringComparison.Ordinal))
                AccountingRunId = null;
            AccountingChronologyKnown &= occurredAt != default;
        }
        if (fillId != Guid.Empty)
            _accountingFillIds.Add(fillId);
    }

    public void RestoreAccountingIdentity(Guid? securityId, string? runId, IEnumerable<Guid> fillIds)
    {
        AccountingSecurityId = securityId is { } id && id != Guid.Empty ? id : null;
        AccountingRunId = string.IsNullOrWhiteSpace(runId) ? null : runId.Trim();
        AccountingChronologyKnown = true;
        foreach (var fillId in fillIds.Where(static id => id != Guid.Empty))
            _accountingFillIds.Add(fillId);
    }
    public decimal OpenCostBasis { get; private set; }
    public long LotVersion { get; private set; }

    /// <summary>
    /// The position includes an unverified historical basis or a residual whose typed opening was withheld.
    /// Reconstruction from retained session provenance carries this restriction until truly flat.
    /// </summary>
    public bool RequiresAccountingReclassification { get; set; }

    /// <summary>
    /// Amount borrowed from the broker to fund this position.
    /// Non-zero only for margin accounts; zero for cash accounts and short positions.
    /// </summary>
    public decimal MarginBorrowed { get; set; }

    /// <summary>
    /// The position was opened by face-value fills whose clean prices are quoted as a
    /// percentage of par, so <see cref="CostBasis"/> and <see cref="MarketPrice"/> are held
    /// in dollars per unit of face and incoming market quotes must be normalized by 100
    /// before being stored (see <c>PaperTradingPortfolio.UpdateMarketPrice</c>).
    /// </summary>
    public bool UsesFaceValuePricing { get; set; }

    /// <summary>
    /// Accrued interest paid on purchases of a face-value position and not yet relieved by a
    /// sale: the balance carried in Accrued Interest Receivable for this symbol. Purchased
    /// interest is a receivable, never part of <see cref="CostBasis"/>.
    /// </summary>
    public decimal PurchasedAccruedInterest { get; set; }

    /// <summary>
    /// Multiplier that turns quantity × quote into money in this book: the contract multiplier
    /// for listed premium contracts such as options and warrants (premium is paid in full) and for
    /// variation-margin futures (which scale only their settled price changes by it), and 1 for
    /// everything else, including futures restored from notional-model records.
    /// <see cref="CostBasis"/>, <see cref="MarketPrice"/> and lot entry prices stay per-unit
    /// quotes, because every downstream reader applies the multiplier to them itself; only the
    /// money amounts (<see cref="OpenCostBasis"/>, market value, P&amp;L, cash) are scaled.
    /// Distinct from <see cref="ContractMultiplier"/>, the exposure multiplier risk reads.
    /// </summary>
    public decimal EconomicMultiplier { get => _economicMultiplier; init => _economicMultiplier = value; }

    private decimal _economicMultiplier = 1m;

    /// <summary>True when the entry holds nothing: zero quantity and no open lots.</summary>
    public bool IsFlatWithoutLots => Quantity == 0m && _lots.Count == 0;

    /// <summary>
    /// Re-bases the money scale and booking model of a flat entry that is kept only for its fund
    /// attribution, so the next holding opens on its own terms. Refused while anything is held,
    /// because lots and cost basis are priced on the existing scale and model.
    /// </summary>
    public void ResetMoneyScale(decimal economicMultiplier, bool usesVariationMargin = false)
    {
        if (economicMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(economicMultiplier), "Money multiplier must be positive.");
        if (!IsFlatWithoutLots)
            throw new InvalidOperationException($"Cannot change the money scale of '{Symbol}' while it holds a position.");
        _economicMultiplier = economicMultiplier;
        _usesVariationMargin = usesVariationMargin;
        LastSettlementPrice = 0m;
    }

    /// <summary>
    /// The position settles daily variation margin (futures): no notional was paid to open it,
    /// cash moves only by the price change since <see cref="LastSettlementPrice"/>, and its
    /// equity contribution is that unsettled change rather than its notional value.
    /// </summary>
    public bool UsesVariationMargin { get => _usesVariationMargin; init => _usesVariationMargin = value; }

    private bool _usesVariationMargin;

    /// <summary>
    /// For a variation-margin position, the per-unit price its unsettled change is measured from:
    /// the last daily settlement, or the quantity-weighted fill price of contracts added since.
    /// </summary>
    public decimal LastSettlementPrice { get; set; }

    /// <summary>
    /// The per-unit basis the position reports: its entry cost, or for a variation-margin position
    /// the price it is carried at after settlement. Readers recover the mark as basis plus
    /// unrealized P&amp;L over quantity × multiplier, which only holds against the carried price.
    /// </summary>
    public decimal ReportedCostBasis => UsesVariationMargin ? LastSettlementPrice : CostBasis;

    /// <summary>Money value of <paramref name="quantity"/> units at a per-unit <paramref name="price"/>.</summary>
    public decimal Money(decimal quantity, decimal price) => quantity * price * EconomicMultiplier;

    /// <summary>
    /// What one unit of this position's lots measures, retained from resolved fill metadata.
    /// Symbol-only legacy reports retain the explicit shares assumption.
    /// </summary>
    public SecurityLotUnitKind LotUnit { get; init; } = SecurityLotUnitKind.Shares;

    /// <summary>The open lot book in the shared lot shape.</summary>
    public IReadOnlyList<IUnitizedLot> UnitizedLots => _lots;

    /// <summary>When the oldest open lot was opened, or <see langword="null"/> when none is open.</summary>
    public DateTimeOffset? EarliestLotOpenedAt => _lots.Count == 0 ? null : _lots.Min(static lot => lot.OpenedAt);

    /// <summary>Unsigned exposure magnitude (|qty| × price); use <see cref="SignedMarketValue"/> for equity math.</summary>
    public decimal MarketValue => Money(Math.Abs(Quantity), MarketPrice);

    /// <summary>
    /// Signed market value: positive for longs, negative for shorts (a liability). For a
    /// variation-margin position it is only the unsettled change since the last settlement,
    /// because no notional was paid; <see cref="MarketValue"/> still reports the exposure.
    /// </summary>
    public decimal SignedMarketValue => UsesVariationMargin
        ? Money(Quantity, MarketPrice - LastSettlementPrice)
        : Money(Quantity, MarketPrice);

    /// <summary>
    /// Unrealised P&amp;L uses exact retained basis; the display's weighted unit quote
    /// cannot reconstruct the remaining basis after fractional relief. The retained basis is
    /// held per contract, so it is scaled by the multiplier like market value. A
    /// variation-margin position has settled everything up to its last settlement, so only the
    /// change since then is unrealised.
    /// </summary>
    public decimal UnrealisedPnl => UsesVariationMargin
        ? Money(Quantity, MarketPrice - LastSettlementPrice)
        : Math.Sign(Quantity) * (MarketValue - OpenCostBasis);

    public IReadOnlyList<PositionLotEntry> GetLots() => _lots
        .Select(static lot => new PositionLotEntry(
            lot.LotId,
            Math.Abs(lot.OpenQuantity),
            lot.EntryPrice,
            lot.OpenedAt))
        .ToArray();

    public bool UsesCanonicalLotOrdering { get; init; }
    public Guid? CanonicalOpeningLotRecordId { get; set; }
    public string? CanonicalOpeningLotLabel { get; set; }

    public void RestoreLot(string lotId, decimal quantity, decimal entryPrice, decimal exactBasis, DateTimeOffset openedAt, Guid canonicalRecordId)
    {
        _lots.Add(new PositionLot(this, lotId, quantity, entryPrice, openedAt, exactBasis, canonicalRecordId));
        Quantity += quantity;
        OpenCostBasis = _lots.Sum(lot => lot.OpenCostBasis);
        CostBasis = OpenCostBasis / Quantity;
        LotVersion++;
    }

    public void AddLot(decimal signedQuantity, decimal entryPrice, DateTimeOffset openedAt)
    {
        if (signedQuantity == 0m)
        {
            return;
        }

        _lots.Add(new PositionLot(this, UsesCanonicalLotOrdering ? CanonicalOpeningLotLabel! : $"lot-{Guid.NewGuid():N}", signedQuantity, entryPrice, openedAt,
            canonicalTaxLotRecordId: UsesCanonicalLotOrdering ? CanonicalOpeningLotRecordId : null));
        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        LotVersion++;
    }

    /// <summary>
    /// Rescales every open lot by the corporate-action factors applied to the aggregate
    /// position, preserving lot identity and open time. Keeping the lot book in step with
    /// <see cref="Quantity"/>/<see cref="CostBasis"/> is required for lot consumption: a
    /// desynced book would push post-split sells into the shortfall fallback and fabricate
    /// cost basis at the aggregate average.
    /// </summary>
    /// <summary>
    /// Rescales every owner tally by the corporate-action quantity factor, keeping fund
    /// attribution in step with the position it describes.
    /// </summary>
    public void RescaleOwnerQuantities(decimal quantityFactor)
    {
        if (quantityFactor == 1m || _ownerQuantities.Count == 0)
        {
            return;
        }

        foreach (var owner in _ownerQuantities.Keys.ToArray())
        {
            var rescaled = _ownerQuantities[owner] * quantityFactor;
            if (rescaled == 0m)
            {
                _ownerQuantities.Remove(owner);
                continue;
            }

            _ownerQuantities[owner] = rescaled;
        }
    }

    public void RescaleLots(decimal quantityFactor, decimal priceFactor, decimal adjustedQuantity, decimal? adjustedTotalCostBasis)
    {
        if (quantityFactor == 1m && priceFactor == 1m
            && (!adjustedTotalCostBasis.HasValue || adjustedTotalCostBasis.Value == OpenCostBasis))
        {
            return;
        }

        var bases = RescaledLotBases(adjustedTotalCostBasis);
        var rescaled = _lots
            .Select(lot => new PositionLot(
                this,
                lot.LotId,
                lot.OpenQuantity * quantityFactor,
                lot.EntryPrice * priceFactor,
                lot.OpenedAt,
                bases?.GetValueOrDefault(lot.LotId)))
            .ToList();

        if (adjustedTotalCostBasis.HasValue && rescaled.Count > 0)
        {
            // Keep the aggregate and its lot book equal even when each scaled quantity
            // rounds. No basis-bearing lot may silently vanish below decimal precision.
            rescaled[^1].OpenQuantity += adjustedQuantity - rescaled.Sum(static lot => lot.OpenQuantity);
            if (rescaled.Any(lot => lot.OpenQuantity == 0m
                ? lot.OpenCostBasis != 0m
                : Math.Sign(lot.OpenQuantity) != Math.Sign(adjustedQuantity)))
                throw new InvalidOperationException("The corporate action cannot retain the lot quantities and carrying basis at decimal precision.");
        }

        _lots.Clear();
        _lots.AddRange(rescaled.Where(static lot => lot.OpenQuantity != 0m));
        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        LotVersion++;
    }

    private Dictionary<string, decimal>? RescaledLotBases(decimal? adjustedTotalCostBasis)
    {
        if (adjustedTotalCostBasis is not { } target)
            return null; // Legacy adjusters supply only their existing quantity/quote factors.

        var current = OpenCostBasis;
        if (target == current)
            return _lots.ToDictionary(static lot => lot.LotId, static lot => lot.OpenCostBasis);
        if (current <= 0m || target < 0m)
            throw new InvalidOperationException("An exact corporate-action basis change requires a positive retained basis pool and a non-negative result.");

        // Existing lot basis is the allocation weight. The shared pool allocator keeps
        // every result non-negative and assigns the exact total's rounding remainder.
        var weights = _lots.Where(static lot => lot.OpenCostBasis > 0m)
            .Select(static lot => new LotSlice<PositionLot>(lot, lot.OpenCostBasis, true)).ToArray();
        return LotBasisRelief.Allocate(weights, static lot => lot.OpenCostBasis,
                static lot => lot.OpenCostBasis, new LotBasisPool(current, target))
            .ToDictionary(static slice => slice.Lot.LotId, static slice => slice.CostBasis);
    }

    public decimal ConsumeLots(decimal quantity, PositionLotSelectionMethod method, bool isCoveringShort)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        var consumption = LotConsumption.Consume(
            OrderLots(method, isCoveringShort),
            quantity,
            static lot => Math.Abs(lot.OpenQuantity));

        var removedCostBasis = 0m;
        var relief = LotBasisRelief.Allocate(consumption.Slices,
            static lot => Math.Abs(lot.OpenQuantity), static lot => lot.OpenCostBasis);
        foreach (var slice in relief)
        {
            removedCostBasis += slice.CostBasis;

            if (slice.ClosesLot)
            {
                _lots.Remove(slice.Lot);
            }
            else
            {
                slice.Lot.OpenCostBasis -= slice.CostBasis;
                slice.Lot.OpenQuantity = slice.Lot.OpenQuantity > 0m
                    ? slice.Lot.OpenQuantity - slice.Quantity
                    : slice.Lot.OpenQuantity + slice.Quantity;
            }
        }

        if (consumption.Shortfall > 0m)
        {
            // No matching lot records remain; carry the unmatched remainder at the
            // position-level average cost (legacy positions without lot history).
            removedCostBasis += Money(consumption.Shortfall, CostBasis);
        }

        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        CostBasis = CalculateRemainingLotCostBasis(isCoveringShort);
        LotVersion++;

        return removedCostBasis;
    }

    private decimal CalculateRemainingLotCostBasis(bool isCoveringShort)
    {
        var remainingLots = isCoveringShort
            ? _lots.Where(static lot => lot.OpenQuantity < 0m)
            : _lots.Where(static lot => lot.OpenQuantity > 0m);

        var remainingQuantity = remainingLots.Sum(static lot => Math.Abs(lot.OpenQuantity));
        if (remainingQuantity == 0m)
        {
            return 0m;
        }

        var remainingNotional = remainingLots.Sum(static lot => lot.OpenCostBasis);
        return remainingNotional / (remainingQuantity * EconomicMultiplier);
    }

    private IEnumerable<PositionLot> OrderLots(PositionLotSelectionMethod method, bool isCoveringShort)
    {
        var candidates = isCoveringShort
            ? _lots.Where(static lot => lot.OpenQuantity < 0m)
            : _lots.Where(static lot => lot.OpenQuantity > 0m);

        return method switch
        {
            PositionLotSelectionMethod.Fifo when UsesCanonicalLotOrdering => candidates
                .OrderBy(lot => DateOnly.FromDateTime(lot.OpenedAt.UtcDateTime)).ThenBy(lot => lot.CanonicalTaxLotRecordId
                    ?? throw new InvalidOperationException("Canonical FIFO requires a retained durable lot identity.")),
            PositionLotSelectionMethod.Fifo => candidates.OrderBy(static lot => lot.OpenedAt),
            PositionLotSelectionMethod.Lifo => candidates.OrderByDescending(static lot => lot.OpenedAt),
            PositionLotSelectionMethod.Hifo => candidates.OrderByDescending(static lot => lot.EntryPrice).ThenBy(static lot => lot.OpenedAt),
            _ => candidates.OrderBy(static lot => lot.OpenedAt),
        };
    }

    /// <summary>
    /// Contract multiplier for this position, taken from the fills that opened it. Cash,
    /// P&amp;L and market value are scaled by it, so a fill with a different multiplier is
    /// refused while the position is open (see <c>ApplyFillToAccount</c>).
    /// </summary>
    public decimal ContractMultiplier { get; private set; } = 1m;

    /// <summary>Signed quantity attributed to each owning fund account.</summary>
    public IReadOnlyDictionary<string, decimal> OwnerQuantities => _ownerQuantities;

    /// <summary>
    /// True while some fund still holds a non-zero position here, even if they cancel out
    /// in aggregate. Offsetting fund books are exposure, not an absence of it.
    /// </summary>
    public bool HasOwnerExposure => _ownerQuantities.Count > 0;

    /// <summary>
    /// Records which fund a fill belonged to and the exposure multiplier risk reads. Attribution
    /// only — the lot book, cost basis, realised P&amp;L and cash are untouched here (the money
    /// scale lives on <see cref="EconomicMultiplier"/>), while the read path gains the ownership
    /// the shared execution account cannot express.
    /// The tallies always sum to <see cref="Quantity"/>: each is that fund's net signed
    /// contribution, so a fund that bought and sold the same size nets to zero.
    /// </summary>
    /// <summary>Sets the multiplier of a newly opened position before its first fill is applied.</summary>
    public void InitializeContractMultiplier(decimal contractMultiplier)
    {
        if (contractMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier), "Contract multiplier must be positive.");
        if (Quantity != 0m || _lots.Count > 0 || _ownerQuantities.Count > 0)
            throw new InvalidOperationException("The contract multiplier can only be initialized on an empty position.");
        ContractMultiplier = contractMultiplier;
    }

    public void AttributeFill(string? ownerAccountId, decimal signedQuantity, decimal contractMultiplier)
    {
        if (contractMultiplier > ContractMultiplier)
        {
            ContractMultiplier = contractMultiplier;
        }

        if (string.IsNullOrWhiteSpace(ownerAccountId) || signedQuantity == 0m)
        {
            return;
        }

        var owner = ownerAccountId.Trim();
        var updated = _ownerQuantities.GetValueOrDefault(owner) + signedQuantity;
        if (updated == 0m)
        {
            _ownerQuantities.Remove(owner);
            return;
        }

        _ownerQuantities[owner] = updated;
    }

    public ExecutionPosition ToExecutionPosition() => new(
        Symbol,
        (long)Quantity,
        ReportedCostBasis,
        UnrealisedPnl,
        RealisedPnl)
    {
        OwnerQuantities = new Dictionary<string, decimal>(_ownerQuantities, StringComparer.OrdinalIgnoreCase),
        ContractMultiplier = ContractMultiplier,
        // The record's Quantity is whole shares; owner attribution is not. Carry the
        // unrounded size so the unattributed remainder is a real remainder.
        ExactQuantity = Quantity,
        MarkObservedAtUtc = MarkObservedAtUtc,
        MarkSource = MarkSource
    };
}

/// <summary>
/// One open paper lot. Implements the shared lot shape (<see cref="IUnitizedLot"/>) so the paper
/// book is read the same way as execution, backtesting and ledger lots: the unit and multiplier
/// are read live from the owning <see cref="PaperPosition"/>, which is what fills opened it in, so a
/// contract multiplier recorded after the lot opened is never stale on the lot.
/// </summary>
internal sealed class PositionLot(PaperPosition position, string lotId, decimal openQuantity, decimal entryPrice,
    DateTimeOffset openedAt, decimal? openCostBasis = null, Guid? canonicalTaxLotRecordId = null) : IUnitizedLot
{
    public string LotId { get; } = lotId;
    public Guid? CanonicalTaxLotRecordId { get; } = canonicalTaxLotRecordId;
    public decimal OpenQuantity { get; set; } = openQuantity;
    public decimal EntryPrice { get; } = entryPrice;
    public decimal OpenCostBasis { get; set; } = openCostBasis ?? position.Money(Math.Abs(openQuantity), entryPrice);
    public DateTimeOffset OpenedAt { get; } = openedAt;

    /// <summary>Unsigned open size; <see cref="OpenQuantity"/> keeps the sign (negative for shorts).</summary>
    public decimal Quantity => Math.Abs(OpenQuantity);

    public SecurityLotUnitKind UnitKind => position.LotUnit;

    public decimal Multiplier => position.ContractMultiplier;
}
