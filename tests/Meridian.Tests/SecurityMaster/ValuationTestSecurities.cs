using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The hand-computed golden instruments of the valuation seam, shared by the model, service and
/// PostgreSQL suites so every suite values the same bond and the same swap.
/// </summary>
internal static class ValuationTestSecurities
{
    /// <summary>The valuation date every golden case is pinned to.</summary>
    public static readonly DateOnly AsOfDate = new(2026, 1, 1);

    public static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Par 1,000,000, 5% annual on 30/360, issued 2026-01-01, maturing 2028-01-01: two coupons of
    /// 50,000 and principal at maturity.
    /// </summary>
    public static object BondTerms(DateOnly? maturity = null) => new
    {
        issueDate = AsOfDate,
        maturityDate = maturity ?? new DateOnly(2028, 1, 1),
        par = 1_000_000m,
        couponRate = 5m,
        paymentFrequency = "Annual",
        dayCountConvention = "30/360"
    };

    public static SecurityDetailDto Bond(Guid? securityId = null, string currency = "USD", DateOnly? maturity = null)
        => Detail(securityId ?? Guid.NewGuid(), "Bond", "Golden bond", currency, BondTerms(maturity),
            new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, AsOf.AddYears(-1)));

    public static SecurityDetailDto Equity(Guid? securityId = null)
        => Detail(securityId ?? Guid.NewGuid(), "Equity", "Golden equity", "USD", new { shareClass = "Common" },
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "GLDN", true, AsOf.AddYears(-1)));

    /// <summary>
    /// The pinned swap: receive fixed 4% semiannual, pay <paramref name="indexName"/> + 25 bp quarterly
    /// on 1,000,000 notional, effective <paramref name="effectiveDate"/> for one year, 30/360. With a
    /// 3% current fixing and a flat 4% forward curve the floating leg pays 8,125 then 10,625 x 3.
    /// </summary>
    public static object SwapTerms(DateOnly effectiveDate, string indexName = "SOFR", decimal? currentIndexRate = 0.03m)
    {
        var floatingLeg = new Dictionary<string, object?>
        {
            ["legId"] = "pay-floating",
            ["legType"] = "Floating",
            ["currency"] = "USD",
            ["direction"] = "Pay",
            ["index"] = indexName,
            ["spreadBps"] = 25m,
            ["notional"] = 1_000_000m,
            ["paymentFrequency"] = "Quarterly",
            ["dayCount"] = "30/360",
            ["exchangesPrincipal"] = false
        };
        if (currentIndexRate is not null)
        {
            floatingLeg["currentIndexRate"] = currentIndexRate;
        }

        var fixedLeg = new Dictionary<string, object?>
        {
            ["legId"] = "receive-fixed",
            ["legType"] = "Fixed",
            ["currency"] = "USD",
            ["direction"] = "Receive",
            ["fixedRate"] = 0.04m,
            ["notional"] = 1_000_000m,
            ["paymentFrequency"] = "SemiAnnual",
            ["dayCount"] = "30/360",
            ["exchangesPrincipal"] = false
        };

        return new
        {
            effectiveDate,
            maturityDate = effectiveDate.AddYears(1),
            legs = new object[] { fixedLeg, floatingLeg }
        };
    }

    /// <summary>The pinned swap as the Security Master reads it back: terms go through the F# mapping, as a stored record does.</summary>
    public static SecurityDetailDto FloatingSwap(DateOnly? effectiveDate = null, string indexName = "SOFR", decimal? currentIndexRate = 0.03m, Guid? securityId = null)
    {
        var effective = effectiveDate ?? AsOfDate;
        var projection = new SecurityProjectionRecord(
            SecurityId: securityId ?? Guid.NewGuid(),
            AssetClass: "Swap",
            Status: SecurityStatusDto.Active,
            DisplayName: "Golden swap",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "SWAP-GOLD-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Golden swap", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(SwapTerms(effective, indexName, currentIndexRate)),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "valuation-tests", asOf = AsOf, updatedBy = "valuation-tests" }),
            Version: 1,
            EffectiveFrom: AsOf.AddYears(-1),
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SWAP-GOLD-1", true, AsOf.AddYears(-1))],
            Aliases: []);

        var persisted = SecurityMasterMapping.ToProjection(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)));
        return SecurityMasterMapping.ToDetail(persisted);
    }

    /// <summary>A Discount definition in <paramref name="currency"/> for the model suites (no database).</summary>
    public static SecurityCurveDefinitionDto DiscountDefinition(string key = "USD-DISCOUNT-TEST", string currency = "USD")
        => new(Guid.NewGuid(), key, SecurityCurveKind.Discount, currency, null, null, "Thirty360", SecurityCurveInterpolation.Linear, "test", null, "casey", AsOf);

    public static SecurityCurveDefinitionDto ForwardDefinition(string key = "USD-SOFR-3M", string indexName = "SOFR", string currency = "USD")
        => new(Guid.NewGuid(), key, SecurityCurveKind.Forward, currency, indexName, "3M", "Actual360", SecurityCurveInterpolation.Linear, "test", null, "casey", AsOf);

    public static SecurityCurveVersionDto Version(SecurityCurveDefinitionDto definition, DateOnly asOf, params (DateOnly Date, decimal Value)[] points)
    {
        var dtos = points.Select(static point => new SecurityCurvePointDto(point.Date, point.Value)).ToList();
        return new SecurityCurveVersionDto(
            Guid.NewGuid(), definition.CurveId, definition.CurveKey, 1, asOf, AsOf, null, definition.Source, "casey", dtos,
            SecurityCurveRules.ContentSha256(asOf, dtos));
    }

    /// <summary>The bond golden discount curve: (2026-01-01, 1.00), (2027-01-01, 0.96), (2028-01-01, 0.92).</summary>
    public static (DateOnly Date, decimal Value)[] BondDiscountPoints =>
    [
        (new DateOnly(2026, 1, 1), 1.00m),
        (new DateOnly(2027, 1, 1), 0.96m),
        (new DateOnly(2028, 1, 1), 0.92m)
    ];

    /// <summary>The swap golden discount curve: quarterly pillars from 1.00 down to 0.96.</summary>
    public static (DateOnly Date, decimal Value)[] SwapDiscountPoints =>
    [
        (new DateOnly(2026, 1, 1), 1.00m),
        (new DateOnly(2026, 4, 1), 0.99m),
        (new DateOnly(2026, 7, 1), 0.98m),
        (new DateOnly(2026, 10, 1), 0.97m),
        (new DateOnly(2027, 1, 1), 0.96m)
    ];

    /// <summary>A curve on which every discount factor is 1, so the PV is the undiscounted sum.</summary>
    public static (DateOnly Date, decimal Value)[] UnitDiscountPoints =>
    [
        (new DateOnly(2026, 1, 1), 1.00m),
        (new DateOnly(2028, 1, 1), 1.00m)
    ];

    private static SecurityDetailDto Detail(Guid securityId, string assetClass, string name, string currency, object terms, SecurityIdentifierDto identifier)
        => new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            name,
            currency,
            JsonSerializer.SerializeToElement(new { displayName = name, currency }),
            JsonSerializer.SerializeToElement(terms),
            [identifier],
            [],
            1,
            AsOf.AddYears(-1),
            null);
}
