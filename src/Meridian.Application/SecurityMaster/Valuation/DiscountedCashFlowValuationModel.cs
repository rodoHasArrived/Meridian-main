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

    private const string ForwardCurveRateSourcePrefix = "forwardCurve:";
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

        if (projection.Schedule.Count == 0)
        {
            blockers.Add(new SecurityValuationBlockerDto("NoProjectedFlows", "The projection has no net cash flows on or after the valuation date (no remaining periods, or a multi-leg structure whose directions are unknown)."));
            return Blocked();
        }

        inputs.Add(new SecurityValuationInputDto("SecurityTerms", security.Version.ToString(), security.SecurityId, security.Version));

        // 3. Where each floating leg's rates came from.
        var observability = SecurityValuationObservability.ObservableInputs;
        foreach (var leg in projection.LegSchedules ?? [])
        {
            if (leg.RateSource is null || string.Equals(leg.RateSource, FixedRateSource, StringComparison.Ordinal))
            {
                continue;
            }

            if (leg.RateSource.StartsWith(ForwardCurveRateSourcePrefix, StringComparison.Ordinal))
            {
                inputs.Add(new SecurityValuationInputDto("ForwardCurve", leg.RateSource));
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
        decimal? basis;
        SecurityPriceUnit priceUnit;
        decimal? price;
        if (descriptor.UsesFaceValueLots)
        {
            basis = projection.IsNormalizedPer100 ? 100m : terms?.PrincipalFace;
            priceUnit = SecurityPriceUnit.PercentOfPar;
            price = basis is > 0m ? presentValue / basis.Value * 100m : null;
        }
        else
        {
            basis = terms?.Legs?.FirstOrDefault()?.Notional ?? terms?.PrincipalFace;
            priceUnit = SecurityPriceUnit.CurrencyPerUnit;
            price = basis is > 0m ? presentValue / basis.Value : null;
        }

        if (price is null)
        {
            diagnostics.Add("NoPriceBasis: the terms carry no principal face or leg notional, so no per-unit price is derived; the present value stands on its own.");
            priceUnit = SecurityPriceUnit.Unspecified;
        }

        var marketValue = SecurityValuationMarketValues.Compute(security, request.Quantity, price, priceUnit, diagnostics);

        // 6. Dates and the dirty-value caveat.
        if (version.AsOf < asOfDate)
        {
            diagnostics.Add($"DiscountCurveAsOfPrecedesValuationDate: '{version.CurveKey}' v{version.Version} is dated {version.AsOf:yyyy-MM-dd} and was rebased to {asOfDate:yyyy-MM-dd}.");
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
            ToUtc(version.AsOf),
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
            version = (await _curves.ListVersionsAsync(definition.CurveId, ct).ConfigureAwait(false))
                .FirstOrDefault(candidate => candidate.CurveVersionId == pinnedVersionId);
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
