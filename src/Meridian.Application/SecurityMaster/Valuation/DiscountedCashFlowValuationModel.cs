using System.Globalization;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// The discounted-cash-flow model: the base-scenario projection as of the valuation date (fixed
/// legs from terms, floating legs from the registered forward-curve provider) discounted on a stored
/// Discount curve. The curve comes from the request (pinned by id, optionally by version) or, when
/// none is pinned, from the one Discount definition in the security's currency; ambiguity and
/// missing inputs block, and zero is never substituted. The value is dirty: it includes the full
/// next coupon.
/// </summary>
public sealed class DiscountedCashFlowValuationModel : ISecurityValuationModel
{
    public const string Key = "discounted-cash-flow";
    public const string Version = "v1";
    public const string DiscountRole = "Discount";

    private const string FlatForwardRateSource = "flatForward:lastFixing";
    private const string FixedRateSource = "fixed";

    private readonly ISecurityMasterCashFlowService _cashFlows;
    private readonly ISecurityCurveStore _curves;
    private readonly ILogger<DiscountedCashFlowValuationModel> _logger;

    public DiscountedCashFlowValuationModel(
        ISecurityMasterCashFlowService cashFlows,
        ISecurityCurveStore curves,
        ILogger<DiscountedCashFlowValuationModel> logger)
    {
        _cashFlows = cashFlows ?? throw new ArgumentNullException(nameof(cashFlows));
        _curves = curves ?? throw new ArgumentNullException(nameof(curves));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SecurityValuationModelKind Kind => SecurityValuationModelKind.DiscountedCashFlow;

    public string ModelKey => Key;

    public string ModelVersion => Version;

    public async Task<SecurityValuationResult> ValueAsync(SecurityValuationRequest request, SecurityDetailDto security, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(security);
        var knowledgeAsOf = request.KnownAt ?? DateTimeOffset.UtcNow;
        var asOfDate = DateOnly.FromDateTime(request.AsOf.UtcDateTime.Date);
        var blockers = new List<SecurityValuationBlockerDto>();
        var diagnostics = new List<string>();
        var inputs = new List<SecurityValuationInputDto>();

        SecurityValuationResult Blocked()
            => SecurityValuationResult.Blocked(request, security, Kind, Key, Version, knowledgeAsOf, blockers, inputs, diagnostics);

        // 1. The discount curve: pinned on the request, or the one definition in the security's currency.
        var discount = await ResolveDiscountCurveAsync(request, security, asOfDate, blockers, ct).ConfigureAwait(false);
        if (blockers.Count > 0 || discount is null)
        {
            return Blocked();
        }

        var (definition, version) = discount.Value;
        inputs.Add(new SecurityValuationInputDto(
            "DiscountCurveVersion",
            version.CurveKey,
            version.CurveVersionId,
            version.Version,
            version.ContentSha256,
            ToUtc(version.AsOf),
            version.RecordedAt));

        // 2. The projection as of the valuation date, under the base scenario only.
        var projection = await _cashFlows.GetProjectionAsOfAsync(security.SecurityId, StructuredCashFlowScenario.Base, request.AsOf, ct).ConfigureAwait(false);
        if (projection is null)
        {
            blockers.Add(new SecurityValuationBlockerDto("NoCashFlowSource", "No calculated cash-flow source is assigned to the security, or its flows are client-provided; nothing can be discounted."));
            return Blocked();
        }

        if (projection.BlockedReason is { } reason)
        {
            blockers.Add(new SecurityValuationBlockerDto("ProjectionBlocked", reason));
            return Blocked();
        }

        // The ledger gate refuses to post from a stale or undated source; the valuation reports it.
        switch (projection.Staleness)
        {
            case StructuredCashFlowStaleness.Stale:
                diagnostics.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"CashFlowSourceStale: the cash-flow source was last updated {projection.SourceLastUpdatedUtc:O} (SourceLastUpdatedUtc), beyond the staleness threshold; the ledger gate would not post from it."));
                break;
            case StructuredCashFlowStaleness.Unknown:
                diagnostics.Add("CashFlowSourceFreshnessUnknown: the cash-flow source carries no SourceLastUpdatedUtc, so its freshness cannot be assessed; the ledger gate would not post from it.");
                break;
        }

        if (projection.Schedule.Count == 0)
        {
            blockers.Add(new SecurityValuationBlockerDto("NoProjectedFlows", "The projection has no net cash flows on or after the valuation date (no remaining periods, or a multi-leg structure whose directions are unknown)."));
            return Blocked();
        }

        inputs.Add(new SecurityValuationInputDto("SecurityTerms", security.Version.ToString(), security.SecurityId, security.Version));

        // 3. Where each floating leg's rates came from. The observed as-of is the oldest market input:
        //    the discount curve's as-of, or an older forward curve's.
        var observability = SecurityValuationObservability.ObservableInputs;
        var observedAsOf = version.AsOf;
        foreach (var leg in projection.LegSchedules ?? [])
        {
            if (leg.RateSource is null || string.Equals(leg.RateSource, FixedRateSource, StringComparison.Ordinal))
            {
                continue;
            }

            if (leg.RateSource.StartsWith(SecurityForwardCurve.RateSourcePrefix, StringComparison.Ordinal))
            {
                if (!SecurityForwardCurve.TryReadAsOf(leg.RateSource, out var forwardAsOf))
                {
                    blockers.Add(new SecurityValuationBlockerDto(
                        "UnsupportedInput",
                        $"Leg '{leg.LegId}' names forward-curve provenance '{leg.RateSource}' without a readable as-of date, so the oldest market input cannot be stated."));
                    return Blocked();
                }

                inputs.Add(new SecurityValuationInputDto("ForwardCurve", leg.RateSource, AsOf: ToUtc(forwardAsOf)));
                if (forwardAsOf < observedAsOf)
                {
                    observedAsOf = forwardAsOf;
                }
            }
            else if (string.Equals(leg.RateSource, FlatForwardRateSource, StringComparison.Ordinal))
            {
                var indexName = projection.TermsUsed?.Legs?.FirstOrDefault(candidate => string.Equals(candidate.LegId, leg.LegId, StringComparison.Ordinal))?.IndexName;
                diagnostics.Add($"FloatingForecastFlatAtCurrentIndexRate: leg '{leg.LegId}' projects {indexName ?? "its index"} flat at the last fixing because no forward curve is stored for it.");
                observability = SecurityValuationObservability.UnobservableInputs;
            }
        }

        // 4. Discount the net schedule; a payment past the curve's last pillar blocks.
        var presentValue = 0m;
        foreach (var entry in projection.Schedule)
        {
            var paymentDate = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            var discountFactor = SecurityCurveRules.DiscountFactorAt(version, asOfDate, paymentDate);
            if (discountFactor is null)
            {
                var lastPillar = version.Points.Max(static point => point.Date);
                blockers.Add(new SecurityValuationBlockerDto(
                    "DiscountCurveCoverage",
                    $"Payment on {paymentDate:yyyy-MM-dd} lies beyond the discount curve's last pillar {lastPillar:yyyy-MM-dd} ('{version.CurveKey}' v{version.Version})."));
                return Blocked();
            }

            presentValue += (entry.PrincipalAmount + entry.InterestAmount) * discountFactor.Value;
        }

        presentValue = decimal.Round(presentValue, 2, MidpointRounding.AwayFromZero);

        // 5. A per-unit price on the projected basis.
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        var terms = projection.TermsUsed;
        SecurityPriceUnit priceUnit;
        decimal? price = null;
        decimal? quotedFactor = null;
        if (descriptor.UsesFaceValueLots)
        {
            // The projection walks the outstanding face with whatever factor it selected (a dated
            // schedule point, the scalar, completed contractual payments), so the outstanding face is
            // read from the projection itself: the principal it repays from the valuation date. The
            // percent-of-par price is per 100 of that face, and the market value scales the quantity
            // by the same outstanding over the principal face, so the factor is applied exactly once.
            var face = terms?.PrincipalFace;
            var outstanding = projection.Schedule.Sum(static entry => entry.PrincipalAmount);
            priceUnit = SecurityPriceUnit.PercentOfPar;
            if (face is > 0m && outstanding > 0m)
            {
                price = presentValue / outstanding * 100m;
                quotedFactor = outstanding / face.Value;
            }
            else if (face is > 0m)
            {
                diagnostics.Add(outstanding == 0m
                    ? "NoPriceBasis: the projection repays no principal from the valuation date (the pool is fully repaid, factor 0), so no per-unit price is derived; the present value stands on its own."
                    : string.Create(CultureInfo.InvariantCulture, $"NoPriceBasis: the projection's net principal from the valuation date is {outstanding}, not a positive outstanding face, so no per-unit price is derived; the present value stands on its own."));
                priceUnit = SecurityPriceUnit.Unspecified;
            }
        }
        else
        {
            var basis = terms?.Legs?.FirstOrDefault()?.Notional ?? terms?.PrincipalFace;
            priceUnit = SecurityPriceUnit.CurrencyPerUnit;
            price = basis is > 0m ? presentValue / basis.Value : null;
        }

        if (price is null && priceUnit != SecurityPriceUnit.Unspecified)
        {
            diagnostics.Add("NoPriceBasis: the terms carry no principal face or leg notional, so no per-unit price is derived; the present value stands on its own.");
            priceUnit = SecurityPriceUnit.Unspecified;
        }

        var marketValue = SecurityValuationMarketValues.Compute(security, request.Quantity, price, priceUnit, asOfDate, diagnostics, quotedFactor);

        // 6. Dates and the dirty-value caveat. A pinned version bypasses the as-of and knowledge
        //    filters, so a version dated after the valuation date or recorded after the knowledge
        //    cutoff is used as given and said so.
        if (version.AsOf < asOfDate)
        {
            diagnostics.Add($"DiscountCurveAsOfPrecedesValuationDate: '{version.CurveKey}' v{version.Version} is dated {version.AsOf:yyyy-MM-dd} and was rebased to {asOfDate:yyyy-MM-dd}.");
        }
        else if (version.AsOf > asOfDate)
        {
            diagnostics.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"DiscountCurveAsOfFollowsValuationDate: '{version.CurveKey}' v{version.Version} is dated {version.AsOf:yyyy-MM-dd}, after the valuation date {asOfDate:yyyy-MM-dd}; the pinned version was used as given and discounts at 1 up to its as-of."));
        }

        if (request.KnownAt is { } cutoff && version.RecordedAt > cutoff)
        {
            diagnostics.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"DiscountCurveRecordedAfterKnownAt: '{version.CurveKey}' v{version.Version} was recorded at {version.RecordedAt:O}, after the knowledge cutoff {cutoff:O}; the pinned version was used as given."));
        }

        diagnostics.Add("DirtyValue: the present value includes the full next coupon; clean price and accrued interest are not separated.");

        _logger.LogDebug(
            "Valued security {SecurityId} by discounted cash flow on curve {CurveKey} v{CurveVersion}: PV {PresentValue} {Currency}.",
            security.SecurityId, version.CurveKey, version.Version, presentValue, definition.Currency);

        return new SecurityValuationResult(
            security.SecurityId,
            Kind,
            Key,
            Version,
            request.AsOf,
            knowledgeAsOf,
            security.Currency,
            price,
            priceUnit,
            presentValue,
            request.Quantity,
            marketValue,
            ToUtc(observedAsOf),
            observability,
            inputs,
            diagnostics,
            []);
    }

    private async Task<(SecurityCurveDefinitionDto Definition, SecurityCurveVersionDto Version)?> ResolveDiscountCurveAsync(
        SecurityValuationRequest request,
        SecurityDetailDto security,
        DateOnly asOfDate,
        ICollection<SecurityValuationBlockerDto> blockers,
        CancellationToken ct)
    {
        SecurityValuationCurveInput? pinned = null;
        foreach (var input in request.Curves ?? [])
        {
            if (!string.Equals(input.Role?.Trim(), DiscountRole, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add(new SecurityValuationBlockerDto("UnsupportedInput", $"Curve role '{input.Role}' is not supported; forward curves reach the projection through the registered provider."));
                continue;
            }

            if (pinned is not null)
            {
                blockers.Add(new SecurityValuationBlockerDto("AmbiguousDiscountCurve", "More than one discount curve is pinned on the request."));
                continue;
            }

            pinned = input;
        }

        if (blockers.Count > 0)
        {
            return null;
        }

        SecurityCurveDefinitionDto? definition;
        if (pinned is not null)
        {
            definition = await _curves.GetDefinitionAsync(pinned.CurveId, ct).ConfigureAwait(false);
            if (definition is null)
            {
                blockers.Add(new SecurityValuationBlockerDto("NoDiscountCurve", $"Curve {pinned.CurveId:D} is not defined."));
                return null;
            }

            if (definition.Kind != SecurityCurveKind.Discount)
            {
                blockers.Add(new SecurityValuationBlockerDto("UnsupportedInput", $"Curve '{definition.CurveKey}' is a {definition.Kind} curve, not a discount curve."));
                return null;
            }
        }
        else
        {
            var candidates = await _curves.ListDefinitionsAsync(SecurityCurveKind.Discount, security.Currency, ct: ct).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                blockers.Add(new SecurityValuationBlockerDto("NoDiscountCurve", $"No discount curve is defined for {security.Currency}; define one or pin a curve on the request."));
                return null;
            }

            if (candidates.Count > 1)
            {
                blockers.Add(new SecurityValuationBlockerDto(
                    "AmbiguousDiscountCurve",
                    $"{candidates.Count} discount curves are defined for {security.Currency} ({string.Join(", ", candidates.Select(static candidate => candidate.CurveKey))}); pin one on the request."));
                return null;
            }

            definition = candidates[0];
        }

        if (!string.Equals(definition.Currency, security.Currency, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SecurityValuationBlockerDto("CurrencyMismatch", $"Discount curve '{definition.CurveKey}' is in {definition.Currency}; the security is in {security.Currency}."));
            return null;
        }

        SecurityCurveVersionDto? version;
        if (pinned?.CurveVersionId is { } pinnedVersionId)
        {
            version = await _curves.GetVersionByIdAsync(definition.CurveId, pinnedVersionId, ct).ConfigureAwait(false);
            if (version is null)
            {
                blockers.Add(new SecurityValuationBlockerDto("NoDiscountCurve", $"Curve '{definition.CurveKey}' has no version {pinnedVersionId:D}."));
                return null;
            }
        }
        else
        {
            version = await _curves.GetLatestVersionAsOfAsync(definition.CurveId, asOfDate, request.KnownAt, ct).ConfigureAwait(false);
            if (version is null)
            {
                blockers.Add(new SecurityValuationBlockerDto(
                    "NoDiscountCurve",
                    $"Discount curve '{definition.CurveKey}' has no version dated on or before {asOfDate:yyyy-MM-dd}" + (request.KnownAt is { } knownAt ? $" and recorded by {knownAt:O}" : string.Empty) + "."));
                return null;
            }
        }

        return (definition, version);
    }

    private static DateTimeOffset ToUtc(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
