using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// End-to-end guards for the two term shapes that were written narrower than they were read:
/// the structured-credit factor schedule and the swap leg.
/// <para>
/// Both were type-correct at the top level — a string, an array — while their contents disagreed
/// with every consumer, so a security could be created through the canonical path, pass validation,
/// and still be economically unusable. These tests round-trip C# → F# domain → serialized JSON and
/// then hand the result to the actual read-side resolver, so the two sides are measured against each
/// other rather than each against its own assumptions.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterTermScheduleCodecTests
{
    [Fact]
    public void StructuredCredit_TypedFactorSchedule_SurvivesTheDomainRoundTrip()
    {
        var projection = RoundTrip("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "A-1",
            poolId = "POOL-2026-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            currentFactor = 0.9825m,
            couponOrIndex = "SOFR+250",
            factorSchedule = new object[]
            {
                new { asOfDate = "2026-01-01", factor = 1.0m },
                new { asOfDate = "2026-02-01", factor = 0.9912m },
                new { asOfDate = "2026-03-01", factor = 0.9825m }
            }
        });

        var emitted = projection.AssetSpecificTerms.GetProperty("factorSchedule");
        emitted.ValueKind.Should().Be(JsonValueKind.Array, "the read side accepts only an array");
        emitted.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void StructuredCredit_FactorSchedule_IsReadableByTheCashFlowResolver()
    {
        // The regression this whole change exists for: the serializer wrote free text, the resolver
        // accepts only an array, so FactorAsOf always fell through to the scalar currentFactor and
        // the tranche restated face at one static level for its whole life.
        var projection = RoundTrip("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            currentFactor = 0.9825m,
            couponOrIndex = "SOFR+250",
            factorSchedule = new object[]
            {
                new { asOfDate = "2026-01-01", factor = 1.0m },
                new { asOfDate = "2026-02-01", factor = 0.9912m },
                new { asOfDate = "2026-03-01", factor = 0.9825m }
            }
        });

        var terms = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));

        terms.HasFactorSchedule.Should().BeTrue();
        terms.FactorSchedule.Should().HaveCount(3);
        terms.FactorAsOf(new DateOnly(2026, 2, 15)).Should().Be(0.9912m);
        terms.FactorAsOf(new DateOnly(2026, 3, 31)).Should().Be(0.9825m);

        // Before any schedule point applies, the scalar factor is still the honest answer.
        terms.FactorAsOf(new DateOnly(2025, 12, 31)).Should().Be(0.9825m);
    }

    [Fact]
    public void StructuredCredit_LegacyStringFactorSchedule_IsPreservedRatherThanDropped()
    {
        // Payloads written under the pre-typed contract carry a free-text factorSchedule. It was
        // never machine-readable, so it must not fail the read — but it is operator-entered text and
        // must not silently vanish when the record is re-written under the typed schedule either.
        var projection = RoundTrip("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            currentFactor = 0.9825m,
            couponOrIndex = "SOFR+250",
            factorSchedule = "monthly-trustee"
        });

        projection.AssetSpecificTerms.GetProperty("factorSchedule").ValueKind.Should().Be(JsonValueKind.Array);
        projection.AssetSpecificTerms.GetProperty("factorSchedule").GetArrayLength().Should().Be(0);
        projection.AssetSpecificTerms.GetProperty("factorScheduleNote").GetString().Should().Be("monthly-trustee");
    }

    [Fact]
    public void StructuredCredit_FactorSchedule_ReachesTheEconomicTermsPaydownReader()
    {
        // The accounting-event adapter reads structuredProduct.factorSchedule off the economic-terms
        // document — not off asset-specific terms — and skips any row missing asOfDate, priorFactor,
        // or currentFactor. Fixing only the asset-terms side would have left the paydown coverage
        // gate unsatisfiable, which is what blocked these securities in the first place.
        var projection = RoundTrip("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            couponOrIndex = "SOFR+250",
            factorSchedule = new object[]
            {
                new { asOfDate = "2026-02-01", factor = 0.9912m },
                new { asOfDate = "2026-03-01", factor = 0.9825m }
            }
        });

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var schedule = economic.EconomicTerms
            .GetProperty("structuredProduct")
            .GetProperty("factorSchedule");

        schedule.GetArrayLength().Should().Be(2);

        // The first point pairs against 1.0 — original face, by definition of a pool factor — so the
        // opening paydown is not lost.
        var first = schedule[0];
        first.GetProperty("asOfDate").GetString().Should().StartWith("2026-02-01");
        first.GetProperty("priorFactor").GetDecimal().Should().Be(1.0m);
        first.GetProperty("currentFactor").GetDecimal().Should().Be(0.9912m);

        var second = schedule[1];
        second.GetProperty("priorFactor").GetDecimal().Should().Be(0.9912m);
        second.GetProperty("currentFactor").GetDecimal().Should().Be(0.9825m);
    }

    [Fact]
    public void Swap_LegEconomics_SurviveTheDomainRoundTripAndReachTheResolver()
    {
        // A leg carrying only a rate label is not projectable: the read side has no notional to
        // accrue on and no frequency to schedule against, so it yields nothing at all.
        var projection = RoundTrip("Swap", new
        {
            schemaVersion = 1,
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs = new object[]
            {
                new
                {
                    legId = "fixed-leg",
                    legType = "Fixed",
                    currency = "USD",
                    direction = "Pay",
                    fixedRate = 0.0425m,
                    notional = 25_000_000m,
                    paymentFrequency = "SemiAnnual",
                    dayCount = "30/360",
                    exchangesPrincipal = false
                },
                new
                {
                    legId = "floating-leg",
                    legType = "Floating",
                    currency = "USD",
                    direction = "Receive",
                    index = "SOFR",
                    spreadBps = 35m,
                    currentIndexRate = 0.0512m,
                    notional = 25_000_000m,
                    paymentFrequency = "Quarterly",
                    dayCount = "ACT/360",
                    exchangesPrincipal = false
                }
            }
        });

        var terms = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));

        terms.HasLegs.Should().BeTrue();
        terms.Legs.Should().HaveCount(2);

        var fixedLeg = terms.Legs![0];
        fixedLeg.LegId.Should().Be("fixed-leg");
        fixedLeg.RateKind.Should().Be(CashFlowLegRateKind.Fixed);
        fixedLeg.Direction.Should().Be(CashFlowLegDirection.Pay);
        fixedLeg.FixedRate.Should().Be(0.0425m);
        fixedLeg.Notional.Should().Be(25_000_000m);
        fixedLeg.PaymentFrequency.Should().Be("SemiAnnual");
        fixedLeg.DayCountConvention.Should().Be("30/360");

        var floatingLeg = terms.Legs[1];
        floatingLeg.RateKind.Should().Be(CashFlowLegRateKind.Floating);
        floatingLeg.Direction.Should().Be(CashFlowLegDirection.Receive);
        floatingLeg.IndexName.Should().Be("SOFR");
        floatingLeg.SpreadBps.Should().Be(35m);
        floatingLeg.CurrentIndexRate.Should().Be(0.0512m);
        floatingLeg.Notional.Should().Be(25_000_000m);
        floatingLeg.PaymentFrequency.Should().Be("Quarterly");
        floatingLeg.DayCountConvention.Should().Be("ACT/360");
    }

    [Fact]
    public void Swap_LegsPersistedUnderThePreWideningShape_StillDeserialize()
    {
        // Legs written before per-leg economics existed carry only legType/currency/index/fixedRate.
        // They must keep reading — they simply carry no economics, exactly as before.
        var projection = RoundTrip("Swap", new
        {
            schemaVersion = 1,
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.0425m },
                new { legType = "Floating", currency = "USD", index = "SOFR" }
            }
        });

        var terms = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));

        terms.Legs.Should().HaveCount(2);
        terms.Legs![0].FixedRate.Should().Be(0.0425m);
        terms.Legs[0].Notional.Should().BeNull();
        terms.Legs[1].IndexName.Should().Be("SOFR");
    }

    [Theory]
    [InlineData("Swap", "legs")]
    [InlineData("StructuredCredit", "factorSchedule")]
    [InlineData("DirectLoan", "principalSchedule")]
    [InlineData("DirectLoan", "covenants")]
    public void DeclaredElementShapes_CoverEveryEconomicallyMeaningfulArray(string assetClass, string fieldKey)
    {
        // The schema table declared only top-level types, which is exactly how the factor schedule
        // and the swap leg drifted: both were the right kind of thing carrying the wrong contents.
        // Arrays whose elements carry economics must declare those elements so the codecs can be
        // measured against the contract instead of against each other.
        var field = SecurityAssetTermsSchema.Field(assetClass, fieldKey);

        field.Should().NotBeNull();
        field!.Type.Should().Be(SecurityAssetTermFieldType.Array);
        field.ElementFields.Should().NotBeEmpty(
            $"'{assetClass}.{fieldKey}' carries economics in its elements, so their shape is part of the contract");
    }

    [Theory]
    [InlineData("Swap", "legs")]
    [InlineData("StructuredCredit", "factorSchedule")]
    public void SerializedElements_ConformToTheDeclaredElementShape(string assetClass, string fieldKey)
    {
        var projection = assetClass == "Swap"
            ? RoundTrip("Swap", new
            {
                schemaVersion = 1,
                effectiveDate = "2026-01-15",
                maturityDate = "2031-01-15",
                legs = new object[]
                {
                    new
                    {
                        legId = "fixed-leg",
                        legType = "Fixed",
                        currency = "USD",
                        direction = "Pay",
                        fixedRate = 0.0425m,
                        notional = 25_000_000m,
                        paymentFrequency = "SemiAnnual",
                        dayCount = "30/360",
                        exchangesPrincipal = false
                    }
                }
            })
            : RoundTrip("StructuredCredit", new
            {
                schemaVersion = 1,
                tranche = "A-1",
                collateralType = "CLO",
                originalFace = 1_000_000m,
                couponOrIndex = "SOFR+250",
                factorSchedule = new object[] { new { asOfDate = "2026-02-01", factor = 0.9912m } }
            });

        var declared = SecurityAssetTermsSchema.Field(assetClass, fieldKey)!.ElementFields;
        var element = projection.AssetSpecificTerms.GetProperty(fieldKey)[0];

        // Every required element field must actually be emitted...
        foreach (var required in declared.Where(static f => f.Required))
        {
            element.TryGetProperty(required.Key, out var value).Should().BeTrue(
                $"'{assetClass}.{fieldKey}[].{required.Key}' is declared required");
            value.ValueKind.Should().NotBe(JsonValueKind.Null);
        }

        // ...and nothing may be emitted that the contract does not declare.
        var declaredKeys = declared.Select(static f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var emitted in element.EnumerateObject())
        {
            declaredKeys.Should().Contain(emitted.Name,
                $"'{assetClass}.{fieldKey}[].{emitted.Name}' is serialized but undeclared");
        }
    }

    /// <summary>
    /// Pushes a projection through the canonical write path — C# terms JSON into the F# domain, then
    /// back out through the snapshot serializer — so what comes back is exactly what would be
    /// persisted and replayed.
    /// </summary>
    private static SecurityProjectionRecord RoundTrip(string assetClass, object assetSpecificTerms)
    {
        var projection = new SecurityProjectionRecord(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Test Security",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "TEST-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Test Security", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                asOf = DateTimeOffset.UtcNow,
                updatedBy = "tester"
            }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());

        var record = SecurityMasterMapping.ToRecord(projection);
        return SecurityMasterMapping.ToProjection(new SecurityMasterSnapshotWrapper(record));
    }
}
