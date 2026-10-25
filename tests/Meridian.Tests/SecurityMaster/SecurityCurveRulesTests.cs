using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The pure rules of a stored curve: what a definition and a version must satisfy, the canonical
/// content hash, and the arithmetic a valuation reads a curve with.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityCurveRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);

    [Fact]
    public void Define_NormalizesTheIdentity()
    {
        var normalized = SecurityCurveRules.Normalize(Define(key: "  usd-discount  ", currency: "usd", dayCount: "30/360", description: " curve "));

        normalized.CurveKey.Should().Be("usd-discount");
        normalized.Currency.Should().Be("USD");
        normalized.DayCount.Should().Be("Thirty360", "the convention is stored as the enum name, never as unparsed text");
        normalized.Description.Should().Be("curve");
        normalized.CreatedBy.Should().Be("casey");
    }

    [Fact]
    public void Define_RefusesAnIdentityThatBreaksARule()
    {
        Refuses(() => SecurityCurveRules.Normalize(Define(key: " ")), "key");
        Refuses(() => SecurityCurveRules.Normalize(Define(kind: SecurityCurveKind.Discount, indexName: "SOFR")), "discount curve has no index");
        Refuses(() => SecurityCurveRules.Normalize(Define(kind: SecurityCurveKind.Forward, indexName: null)), "must name its index");
        Refuses(() => SecurityCurveRules.Normalize(Define(dayCount: "ACT/999")), "not a recognized convention");
        Refuses(() => SecurityCurveRules.Normalize(Define(currency: "US")), "three-letter");
        Refuses(() => SecurityCurveRules.Normalize(Define(createdBy: " ")), "actor");
        Refuses(() => SecurityCurveRules.Normalize(Define(source: " ")), "source");
    }

    [Fact]
    public void Publish_SortsThePoints_AndRefusesWhatBreaksARule()
    {
        var discount = Definition(SecurityCurveKind.Discount);
        var sorted = SecurityCurveRules.Normalize(Publish(discount, (new DateOnly(2027, 1, 1), 0.96m), (AsOf, 1m), (new DateOnly(2028, 1, 1), 0.92m)), discount);
        sorted.Points.Select(static point => point.Date).Should().BeInAscendingOrder();
        sorted.PublishedBy.Should().Be("casey");

        Refuses(() => SecurityCurveRules.Normalize(Publish(discount), discount), "at least one point");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (AsOf, 1m), (AsOf, 1m)), discount), "appears twice");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (new DateOnly(2025, 12, 31), 1m)), discount), "on or after the as-of");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (new DateOnly(2027, 1, 1), 0m)), discount), "(0, 1]");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (new DateOnly(2027, 1, 1), 1.01m)), discount), "(0, 1]");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (AsOf, 0.99m)), discount), "as-of date is 1");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (AsOf, 1m)) with { PublishedBy = " " }, discount), "actor");
        Refuses(() => SecurityCurveRules.Normalize(Publish(discount, (AsOf, 1m)) with { CurveId = Guid.NewGuid() }, discount), "curve it names");

        var withTenors = Publish(discount, (new DateOnly(2027, 1, 1), 0.96m), (new DateOnly(2028, 1, 1), 0.92m)) with
        {
            Points = [new SecurityCurvePointDto(new DateOnly(2027, 1, 1), 0.96m, "1Y"), new SecurityCurvePointDto(new DateOnly(2028, 1, 1), 0.92m, "1y")]
        };
        Refuses(() => SecurityCurveRules.Normalize(withTenors, discount), "tenor label");

        var forward = Definition(SecurityCurveKind.Forward, "SOFR");
        Refuses(() => SecurityCurveRules.Normalize(Publish(forward, (AsOf, 4m)), forward), "publish 0.04, not 4");
        SecurityCurveRules.Normalize(Publish(forward, (AsOf, 0.04m), (new DateOnly(2027, 1, 1), -0.001m)), forward).Points.Should().HaveCount(2, "fractions and negative rates are admitted");

        var inflation = Definition(SecurityCurveKind.Inflation, "CPI-U");
        Refuses(() => SecurityCurveRules.Normalize(Publish(inflation, (AsOf, 0m)), inflation), "positive");
    }

    [Fact]
    public void ContentSha256_IsCanonical_StableAndIndependentOfInputOrder()
    {
        var a = new SecurityCurvePointDto(new DateOnly(2027, 1, 1), 0.96m, "1Y");
        var b = new SecurityCurvePointDto(new DateOnly(2028, 1, 1), 0.920m);

        var hash = SecurityCurveRules.ContentSha256(AsOf, [a, b]);

        Sha256Digest.IsCanonical(hash).Should().BeTrue();
        SecurityCurveRules.ContentSha256(AsOf, [b, a]).Should().Be(hash, "order does not change content");
        SecurityCurveRules.ContentSha256(AsOf, [a, b with { Value = 0.92m }]).Should().Be(hash, "trailing zeros do not change content");
        SecurityCurveRules.ContentSha256(AsOf, [a, b with { Value = 0.93m }]).Should().NotBe(hash);
        SecurityCurveRules.ContentSha256(AsOf.AddDays(1), [a, b]).Should().NotBe(hash, "the as-of is part of the content");
        SecurityCurveRules.ContentSha256(AsOf, [a with { Tenor = null }, b]).Should().NotBe(hash, "a tenor label is part of the content");
    }

    [Fact]
    public void ValueAt_MatchesTheForwardCurveArithmetic_AndADiscountCurveRefusesExtrapolation()
    {
        var forwardDefinition = Definition(SecurityCurveKind.Forward, "SOFR");
        var points = new[] { (new DateOnly(2026, 7, 1), 0.05m), (new DateOnly(2026, 1, 1), 0.03m) };
        var forward = ValuationTestSecurities.Version(forwardDefinition, AsOf, points);
        var legacy = new SecurityForwardCurve("SOFR", AsOf, points.Select(static p => new SecurityForwardCurvePoint(p.Item1, p.Item2)).ToList());
        var midpoint = DateOnly.FromDayNumber((new DateOnly(2026, 1, 1).DayNumber + new DateOnly(2026, 7, 1).DayNumber) / 2);

        foreach (var date in new[] { new DateOnly(2025, 6, 1), midpoint, new DateOnly(2026, 3, 15), new DateOnly(2027, 6, 1) })
        {
            SecurityCurveRules.ValueAt(forward, SecurityCurveKind.Forward, date).Should().Be(legacy.RateAt(date), $"the stored curve reads {date} as the seam does");
        }

        SecurityCurveRules.ValueAt(forward, SecurityCurveKind.Forward, midpoint).Should().BeApproximately(0.04m, 0.0002m);
        SecurityCurveRules.ValueAt(forward, SecurityCurveKind.Discount, new DateOnly(2027, 6, 1)).Should().BeNull("a discount factor is never extrapolated");
        SecurityCurveRules.ValueAt(forward with { Points = [] }, SecurityCurveKind.Forward, midpoint).Should().BeNull();

        var pair = ValuationTestSecurities.Version(forwardDefinition, AsOf, (new DateOnly(2027, 1, 1), 0.96m), (new DateOnly(2027, 12, 31), 0.92m));
        SecurityCurveRules.ValueAt(pair, SecurityCurveKind.Discount, new DateOnly(2027, 7, 2)).Should().Be(0.94m, "182 of 364 days");
    }

    [Fact]
    public void DiscountFactorAt_IsOneOnOrBeforeTheValuationDate_InterpolatesRebasesAndRefusesBeyondTheLastPillar()
    {
        var definition = Definition(SecurityCurveKind.Discount);
        var curve = ValuationTestSecurities.Version(definition, AsOf,
            (AsOf, 1.00m), (new DateOnly(2027, 1, 1), 0.96m), (new DateOnly(2027, 12, 31), 0.92m));

        SecurityCurveRules.DiscountFactorAt(curve, AsOf, AsOf).Should().Be(1m);
        SecurityCurveRules.DiscountFactorAt(curve, AsOf, new DateOnly(2025, 12, 1)).Should().Be(1m);
        SecurityCurveRules.DiscountFactorAt(curve, AsOf, new DateOnly(2027, 1, 1)).Should().Be(0.96m);
        SecurityCurveRules.DiscountFactorAt(curve, AsOf, new DateOnly(2027, 7, 2)).Should().Be(0.94m, "the 182/364 midpoint");
        SecurityCurveRules.DiscountFactorAt(curve, AsOf, new DateOnly(2028, 1, 1)).Should().BeNull("beyond the last pillar");

        // Valuing after the first pillar rebases the curve to the valuation date.
        SecurityCurveRules.DiscountFactorAt(curve, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)).Should().Be(0.92m / 0.96m);

        // A curve whose first pillar is after the valuation date discounts from 1 on that date.
        var later = ValuationTestSecurities.Version(definition, AsOf, (new DateOnly(2026, 4, 1), 0.99m), (new DateOnly(2027, 1, 1), 0.96m));
        SecurityCurveRules.DiscountFactorAt(later, AsOf, new DateOnly(2027, 1, 1)).Should().Be(0.96m);
    }

    [Fact]
    public void ToForwardCurve_CarriesKeyVersionAndHashIntoTheProvenance_AndOnlyForForwardCurves()
    {
        var definition = Definition(SecurityCurveKind.Forward, "SOFR");
        var version = ValuationTestSecurities.Version(definition, AsOf, (AsOf, 0.04m), (new DateOnly(2027, 1, 1), 0.04m)) with { Version = 3 };

        var curve = SecurityCurveRules.ToForwardCurve(version, definition);

        curve.IndexName.Should().Be("SOFR");
        curve.AsOfDate.Should().Be(AsOf);
        curve.Points.Should().HaveCount(2);
        curve.Describe().Should().Be($"forwardCurve:SOFR@2026-01-01 ({definition.CurveKey} v3 {version.ContentSha256[..12]})");

        var discount = Definition(SecurityCurveKind.Discount);
        Refuses(() => SecurityCurveRules.ToForwardCurve(ValuationTestSecurities.Version(discount, AsOf, (AsOf, 1m)), discount), "Only a Forward curve");
        Refuses(() => SecurityCurveRules.ToForwardCurve(version, Definition(SecurityCurveKind.Forward, "ESTR")), "does not belong");
    }

    private static void Refuses(Action action, string because)
        => action.Should().Throw<ArgumentException>().WithMessage($"*{because}*");

    private static DefineSecurityCurveRequest Define(
        string key = "USD-DISCOUNT",
        SecurityCurveKind kind = SecurityCurveKind.Discount,
        string currency = "USD",
        string? indexName = null,
        string dayCount = "ACT/360",
        string source = "test",
        string? description = null,
        string? createdBy = "casey")
        => new(key, kind, currency, indexName, null, dayCount, SecurityCurveInterpolation.Linear, source, description, createdBy);

    private static SecurityCurveDefinitionDto Definition(SecurityCurveKind kind, string? indexName = null)
        => new(Guid.NewGuid(), $"USD-{kind}-{indexName ?? "X"}", kind, "USD", indexName, null, "Thirty360", SecurityCurveInterpolation.Linear, "test", null, "casey", DateTimeOffset.UtcNow);

    private static PublishSecurityCurveVersionRequest Publish(SecurityCurveDefinitionDto definition, params (DateOnly Date, decimal Value)[] points)
        => new(definition.CurveId, AsOf, points.Select(static point => new SecurityCurvePointDto(point.Date, point.Value)).ToList(), "test", null, "casey");
}
