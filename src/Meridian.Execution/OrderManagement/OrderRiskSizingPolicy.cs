using System.Globalization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Computes order and amendment risk sizing from explicit request/state evidence. It neither
/// reserves capacity nor changes tracked orders; the admission owner performs those operations.
/// </summary>
internal static class OrderRiskSizingPolicy
{
    internal static OrderRequest StampResolvedOrderSizing(
        OrderRequest request,
        bool usesFaceValuePercentageOfPar) =>
        usesFaceValuePercentageOfPar
            ? request with { Metadata = OrderSizingMetadata.WithFaceValuePercentageOfPar(request.Metadata) }
            : request;

    internal static decimal ResolveContractMultiplier(OrderRequest request)
    {
        const decimal defaultOptionMultiplier = 100m;

        static decimal FromContract(OptionContractIdentity? contract)
        {
            if (contract is null)
            {
                return 1m;
            }

            return decimal.TryParse(
                contract.Multiplier,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0m
                ? parsed
                : defaultOptionMultiplier;
        }

        var multiplier = FromContract(request.OptionContract);
        if (request.Legs is { Count: > 0 } legs)
        {
            foreach (var leg in legs)
            {
                multiplier = Math.Max(multiplier, FromContract(leg.OptionContract));
            }
        }

        return multiplier;
    }

    /// <summary>
    /// Measured value of an order state under the same model the enforced rules use: the
    /// routed notional for dollar-sized orders, otherwise quantity times the order's own
    /// price. Returns null when the state carries no price of its own (a market order),
    /// where only the live mark — which the OMS does not hold — could measure it.
    /// </summary>
    internal static decimal? MeasureOrderValue(
        decimal quantity,
        decimal? limitPrice,
        decimal? stopPrice,
        decimal? routedNotional,
        decimal contractMultiplier = 1m,
        bool usesFaceValuePercentageOfPar = false)
    {
        if (routedNotional is { } notional && notional > 0m)
        {
            return notional;
        }

        var price = limitPrice ?? stopPrice;
        if (price is not { } resolved || resolved <= 0m)
        {
            return null;
        }

        // A contract is not a share. Measuring an option amendment at quantity x premium
        // would assess an increase from 10 to 100 contracts at $5 as a $450 increment
        // instead of $45,000, and the per-order rule would see a $500 order.
        //
        // Scale percentage-of-par before multiplying, matching OrderNotionalResolver: dividing the
        // product instead lets the intermediate overflow on a notional that is perfectly
        // representable, and here that throws inside the amendment probe — before any risk decision
        // or dispatch — so ModifyOrderAsync raises instead of returning a structured refusal.
        var effectivePrice = usesFaceValuePercentageOfPar ? resolved / 100m : resolved;
        return Math.Abs(quantity) * effectivePrice
            * (contractMultiplier > 0m ? contractMultiplier : 1m);
    }

    /// <summary>
    /// True when a modification needs a fresh risk decision. Quantity increases qualify because
    /// they raise exposure. Every supplied limit or stop price also qualifies even when notional
    /// falls: the dangerous direction is side- and order-type-specific, so a side-neutral numeric
    /// increase test lets a sell limit or buy stop be amended through the market without the price
    /// controls seeing it. Presence, rather than equality with the initially read state, also
    /// prevents a concurrent amendment from turning an apparent no-op into an unvalidated price
    /// reversal. Quantity-only reductions still bypass the gate so de-risking is never blocked.
    /// </summary>
    internal static bool RequiresRiskRevalidation(OrderState state, OrderModification modification)
    {
        if (modification.NewQuantity is { } newQuantity)
        {
            // A quantity amendment on a broker-notional order changes the sizing BASIS, so the two
            // numbers are not comparable and "smaller" does not mean smaller. The order's Quantity
            // is dollars (or a placeholder standing in for them), while the amended value is units
            // the gateway sends as qty with no notional field: $2,500 becoming 100 shares of a $100
            // symbol is $10,000 at the venue, and comparing 100 against 2,500 reads it as a
            // reduction and skips the gate on a fourfold increase. Any such amendment revalidates.
            if (state.RoutedNotional is > 0m)
            {
                return true;
            }

            if (Math.Abs(newQuantity) > Math.Abs(state.Quantity))
            {
                return true;
            }
        }

        return modification.NewLimitPrice is not null || modification.NewStopPrice is not null;
    }

    /// <summary>
    /// Reconstructs the order the gateway would hold after <paramref name="modification"/>,
    /// so the pre-trade rules evaluate the proposed order rather than the original.
    /// </summary>
    private static OrderRequest BuildAmendedRequest(OrderState state, OrderModification modification) => new()
    {
        Symbol = state.Symbol,
        Side = state.Side,
        Type = state.Type,
        Quantity = modification.NewQuantity ?? state.Quantity,
        LimitPrice = modification.NewLimitPrice ?? state.LimitPrice,
        StopPrice = modification.NewStopPrice ?? state.StopPrice,
        ClientOrderId = state.OrderId,
        StrategyId = state.StrategyId,
        FundAccountId = state.FundAccountId,
        Metadata = BuildAmendedSizingMetadata(state, modification),
        // Without the derivative identity the rules re-value the amended order as shares,
        // repeating the 1x mistake the multiplier above exists to prevent.
        OptionContract = state.OptionContract,
        Legs = state.Legs
    };

    /// <summary>
    /// The dollar sizing the broker still holds after an amendment. Nulling it instead makes the
    /// exposure provider fall back to <c>Quantity × price</c>, and for a notional order the quantity
    /// field is a placeholder rather than a size — a $2,500 buy carried as <c>Quantity = 1</c>
    /// repriced to a $90 limit would reserve $90, and later orders would clear portfolio ceilings
    /// against exposure understated by more than an order of magnitude. Under-reserving is the
    /// direction that admits a breach, so this errs the other way.
    /// <para>
    /// A price-only amendment cannot change the routed dollars, so they carry over. A quantity
    /// amendment ends the dollar sizing outright: <c>AlpacaBrokerageGateway.ModifyOrderAsync</c>
    /// serializes <c>NewQuantity</c> as <c>qty</c> and sends no notional field, so the replacement
    /// the venue holds is unit-sized. Keeping the old dollars past that point under-reserves rather
    /// than over-reserves — a $2,500 notional order amended to 100 shares of a $100 symbol leaves
    /// the broker holding $10,000 against a $2,500 reserve — so the classification is dropped and
    /// every unit-based rail, the fat-finger quantity ceiling included, applies to the new size.
    /// </para>
    /// </summary>
    internal static decimal? ResolveAmendedRoutedNotional(OrderState state, OrderModification modification)
    {
        if (state.RoutedNotional is not { } routedNotional || routedNotional <= 0m)
        {
            return null;
        }

        return modification.NewQuantity is null ? routedNotional : null;
    }

    /// <summary>
    /// Restores the order's sizing classification onto the rebuilt amendment. An amended request is
    /// reconstructed from <see cref="OrderState"/> rather than carried over, so anything the rules
    /// need in order to read <c>Quantity</c> correctly has to be put back explicitly — a price-only
    /// amendment does not change how the venue sizes the order.
    /// <para>
    /// Both classifications matter and they are not the same. Face value says the quantity is par
    /// and the price is a percentage of it; broker-native notional says the quantity field carries
    /// <em>dollars</em> and the gateway discards it. Dropping the latter makes every rule that reads
    /// quantity treat a dollar amount as a share count, so a $2,500 notional order cannot amend its
    /// price under a 1,000-unit fat-finger ceiling — an order the venue would leave sized exactly as
    /// it was, refused for a size it does not have.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildAmendedSizingMetadata(
        OrderState state,
        OrderModification modification)
    {
        var metadata = state.UsesFaceValuePercentageOfPar
            ? new Dictionary<string, string>(OrderSizingMetadata.WithFaceValuePercentageOfPar(metadata: null))
            : null;

        // Dropped for a quantity amendment for the reason ResolveAmendedRoutedNotional gives: the
        // gateway sends the new quantity as qty and no notional field, so the replacement is
        // unit-sized. Carrying the marker would also make the fat-finger quantity limb read the new
        // share count as dollars and skip its ceiling on the one amendment that changed the size.
        if (modification.NewQuantity is not null
            || state.RoutedNotional is not { } routedNotional
            || routedNotional <= 0m)
        {
            return metadata;
        }

        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The canonical key, written as the decimal the reader treats as the notional itself
        // rather than as a boolean over a quantity this request may be amending.
        metadata[BrokerNotionalMetadata.Keys[0]] =
            routedNotional.ToString("G29", CultureInfo.InvariantCulture);
        return metadata;
    }

    /// <summary>
    /// Builds the request the risk rules should evaluate for an amendment. The exposure
    /// snapshot already reserves the working order at its current size, so evaluating the
    /// full amended order would double-count it — raising a $1k working buy to $2k would
    /// project $3k. When both sizes are measurable, this returns a probe carrying only the
    /// incremental value, so snapshot + probe equals the post-amendment book.
    /// <para>
    /// An amendment that adds no exposure stamps an increment of zero, whatever its side: it adds
    /// nothing to a book the snapshot already reserves. This is what keeps unchanged and reducing
    /// amendments routable — projecting the full order on top of the reservation it replaces reads
    /// an unchanged $10,000 sell as $20,000, and a gross ceiling between the two refuses it,
    /// tripping the breaker at Critical severity over an amendment that changed no exposure at all.
    /// </para>
    /// <para>
    /// Two kinds of order prove that differently, so they are decided by different evidence rather
    /// than by one test with exceptions attached.
    /// </para>
    /// <para>
    /// <b>Capped buy</b> — the limit is the price paid, so the measured value <em>is</em> exposure.
    /// The increment is the measured difference, floored at zero.
    /// </para>
    /// <para>
    /// <b>Everything else</b> (any sell, a stop-market trigger, an unpriced order) — the order's own
    /// price is not what it pays, so measured value is not exposure and the two can move in opposite
    /// directions: a sell amended from 10 shares at $100 to 100 at $1 measures downward while real
    /// exposure at a $100 mark grows tenfold. What both sides share is the mark, and exposure is
    /// quantity × mark, so exposure moves with <em>quantity</em> alone. A non-increasing quantity
    /// proves no added exposure however the entered price moved — a 100-share sell repriced $100 to
    /// $110 is the same shares against the same mark, and charging the full amended order for it
    /// reads $21,000 against a $10,000 book. An increasing quantity cannot be valued here at all,
    /// because the increase is Δquantity × mark and the OMS has no mark, so it carries the whole
    /// amended order for the rules to price. That double-counts the existing reservation, but
    /// over-reserving in this one direction can only refuse an amendment, never admit one that
    /// breaches a ceiling.
    /// </para>
    /// </summary>
    internal static OrderRequest BuildAmendmentProbe(OrderState state, OrderModification modification)
    {
        var amended = BuildAmendedRequest(state, modification);
        var probeMetadata = amended.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(amended.Metadata, StringComparer.OrdinalIgnoreCase);
        probeMetadata[RiskEscalationQueueService.EvaluationOnlyMetadataKey] = "true";

        var pricePaidIsCapped = state.Side == OrderSide.Buy
            && state.LimitPrice is > 0m
            && amended.LimitPrice is > 0m;

        var currentValue = MeasureOrderValue(
            state.Quantity,
            state.LimitPrice,
            state.StopPrice,
            state.RoutedNotional,
            state.ContractMultiplier,
            state.UsesFaceValuePercentageOfPar);
        var amendedValue = MeasureOrderValue(
            amended.Quantity,
            amended.LimitPrice,
            amended.StopPrice,
            routedNotional: null,
            state.ContractMultiplier,
            state.UsesFaceValuePercentageOfPar);
        if (currentValue is not { } current || amendedValue is not { } proposed)
        {
            // Genuinely unmeasurable on both sides of the comparison: nothing to difference, so the
            // rules price the whole amended order at the mark and over-reserve.
            return amended with { Metadata = probeMetadata };
        }

        // Quantity stays the FULL amended quantity so the position-limit rule projects the
        // real post-amendment position; only the notional-based rules read the incremental
        // value, because the snapshot already reserves the working order's current size.
        //
        // An amendment that does not raise the order's own measured value adds nothing to the book,
        // whatever its side. Falling through to the full amended order projects it *on top of* a
        // snapshot that still reserves the original — an unchanged $10,000 sell reads as $20,000,
        // and a $10,000 buy cut to $9,000 reads as $19,000 — so a gross ceiling between the two
        // refuses the amendment, and at Critical severity trips the breaker, over an amendment that
        // lowered exposure or changed none of it.
        //
        // That over-reservation was harmless while only *increases* were revalidated, because
        // over-reserving an increase can refuse but never admit. Revalidating every supplied price
        // brought unchanged and reducing amendments down the same path, where the same arithmetic
        // blocks the directions a desk most needs to stay open.
        //
        // Which comparison proves "adds no exposure" depends on whether the order's own price is
        // what it pays, so the two cases are decided by different evidence rather than by one test
        // with exceptions bolted on:
        //
        //   Capped buy — the limit IS the price paid, so measured value is exact. The increment is
        //   simply the measured difference, floored at zero for a reduction or an unchanged price.
        //
        //   Everything else (any sell, a stop-market trigger, an unpriced order) — the order's own
        //   price is not what it pays, so measured value is not exposure. What both sides of the
        //   amendment share is the mark, and exposure is quantity x mark, so exposure moves with
        //   QUANTITY alone. A non-increasing quantity therefore proves no added exposure however
        //   the entered price moved: a 100-share sell repriced $100 -> $110 is the same 100 shares
        //   against the same mark, and charging the full amended order for it reads $21,000 against
        //   a $10,000 book. An increasing quantity cannot be valued here at all — the increase is
        //   Δquantity x mark and the OMS has no mark — so it goes to the rules whole.
        if (pricePaidIsCapped)
        {
            var increment = proposed > current ? proposed - current : 0m;
            probeMetadata[RiskEscalationQueueService.IncrementalNotionalMetadataKey] =
                increment.ToString("G29", CultureInfo.InvariantCulture);
            return amended with { Metadata = probeMetadata };
        }

        if (Math.Abs(amended.Quantity) > Math.Abs(state.Quantity))
        {
            // Priced at the mark by the rules, double-counting the working order's reservation.
            // Over-reserving is the conservative answer in this one direction: it can refuse an
            // amendment, never admit one that breaches a ceiling.
            return amended with { Metadata = probeMetadata };
        }

        probeMetadata[RiskEscalationQueueService.IncrementalNotionalMetadataKey] =
            0m.ToString("G29", CultureInfo.InvariantCulture);
        return amended with { Metadata = probeMetadata };
    }

    internal static decimal? ResolveBrokerageOrderNotional(GatewayResolvedOrder order) =>
        order.Sizing is OrderSizing.BrokerNotional notional
            ? notional.Amount
            : ResolveQuantityNotional(order.Request, order.Sizing is OrderSizing.FaceValuePercentageOfPar);

    internal static decimal? ResolveBrokerageOrderNotional(OrderRequest request)
    {
        var faceValue = OrderSizingMetadata.UsesFaceValuePercentageOfPar(request.Metadata);
        if (!faceValue && BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is { } routedNotional)
            return routedNotional;

        return ResolveQuantityNotional(request, faceValue);
    }

    private static decimal? ResolveQuantityNotional(OrderRequest request, bool faceValue)
    {
        // The package's net limit does not establish the gross value of its individual
        // legs. Without leg pricing this configuration limit cannot authorize the order.
        if (request.Legs is { Count: > 0 })
            return null;

        var price = request.Type switch
        {
            OrderType.Limit or OrderType.StopLimit or OrderType.LimitOnOpen or OrderType.LimitOnClose => request.LimitPrice,
            OrderType.StopMarket => request.StopPrice,
            _ => request.LimitPrice ?? request.StopPrice
        };
        if (price is null or <= 0m)
            return null;

        var multiplier = faceValue ? 1m : ResolveContractMultiplier(request);
        if (!faceValue && request.OptionContract is null
            && SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.OccOptionSymbol, request.Symbol, out _))
            multiplier = 100m;

        return Math.Abs(request.Quantity) * (faceValue ? price.Value / 100m : price.Value) * multiplier;
    }
}
