using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Exercises swap economics through the production writer and snapshot serializer before using
/// the cash-flow reader. Reader-only fixtures cannot detect terms lost during persistence.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SwapLegRoundTripTests
{
    [Fact]
    public void CompleteLegEconomics_SurviveTheDomainRoundTripAndReachTheResolver()
    {
        var projection = RoundTrip(new object[]
        {
            new
            {
                legId = "fixed-pay",
                legType = "Fixed",
                currency = "USD",
                direction = "Pay",
                fixedRate = 0.0425m,
                notional = 25_000_000m,
                paymentFrequency = "SemiAnnual",
                dayCount = "30/360",
                exchangesPrincipal = true
            },
            new
            {
                legId = "floating-receive",
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
        });

        var terms = Resolve(projection);
        terms.Legs.Should().HaveCount(2);
        var fixedLeg = terms.Legs![0];
        fixedLeg.LegId.Should().Be("fixed-pay");
        fixedLeg.RateKind.Should().Be(CashFlowLegRateKind.Fixed);
        fixedLeg.Direction.Should().Be(CashFlowLegDirection.Pay);
        fixedLeg.FixedRate.Should().Be(0.0425m);
        fixedLeg.Notional.Should().Be(25_000_000m);
        fixedLeg.PaymentFrequency.Should().Be("SemiAnnual");
        fixedLeg.DayCountConvention.Should().Be("30/360");
        fixedLeg.ExchangesPrincipal.Should().BeTrue();

        var floatingLeg = terms.Legs[1];
        floatingLeg.LegId.Should().Be("floating-receive");
        floatingLeg.RateKind.Should().Be(CashFlowLegRateKind.Floating);
        floatingLeg.Direction.Should().Be(CashFlowLegDirection.Receive);
        floatingLeg.IndexName.Should().Be("SOFR");
        floatingLeg.SpreadBps.Should().Be(35m);
        floatingLeg.CurrentIndexRate.Should().Be(0.0512m);
        floatingLeg.Notional.Should().Be(25_000_000m);
        floatingLeg.PaymentFrequency.Should().Be("Quarterly");
        floatingLeg.DayCountConvention.Should().Be("ACT/360");
        floatingLeg.ExchangesPrincipal.Should().BeFalse();

        var declared = SecurityAssetTermsSchema.ElementFields("Swap", "legs");
        declared.Should().NotBeEmpty();
        foreach (var emittedLeg in projection.AssetSpecificTerms.GetProperty("legs").EnumerateArray())
        {
            emittedLeg.EnumerateObject().Select(static property => property.Name)
                .Should().BeEquivalentTo(declared.Select(static field => field.Key));
            foreach (var field in declared)
            {
                var value = emittedLeg.GetProperty(field.Key);
                if (value.ValueKind == JsonValueKind.Null)
                {
                    field.Required.Should().BeFalse($"required swap leg field '{field.Key}' must have a value");
                    continue;
                }

                var shapeMatches = field.Type switch
                {
                    SecurityAssetTermFieldType.String => value.ValueKind == JsonValueKind.String,
                    SecurityAssetTermFieldType.Decimal => value.ValueKind == JsonValueKind.Number,
                    SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    _ => false
                };
                shapeMatches.Should().BeTrue($"swap leg field '{field.Key}' must serialize as its declared {field.Type} shape");
            }
        }

        var replayed = SecurityMasterMapping.ToProjection(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)));
        replayed.AssetSpecificTerms.GetRawText().Should().Be(projection.AssetSpecificTerms.GetRawText());
    }

    [Fact]
    public void LegacyFourFieldLegs_RemainReadableWithoutInventingEconomics()
    {
        var terms = Resolve(RoundTrip(new object[]
        {
            new { legType = "Fixed", currency = "USD", index = (string?)null, fixedRate = (decimal?)0.0425m },
            new { legType = "Floating", currency = "USD", index = "SOFR", fixedRate = (decimal?)null }
        }));

        terms.Legs.Should().HaveCount(2);
        terms.Legs![0].FixedRate.Should().Be(0.0425m);
        terms.Legs[1].IndexName.Should().Be("SOFR");
        foreach (var leg in terms.Legs)
        {
            leg.Direction.Should().BeNull();
            leg.Notional.Should().BeNull();
            leg.SpreadBps.Should().BeNull();
            leg.CurrentIndexRate.Should().BeNull();
            leg.PaymentFrequency.Should().BeNull();
            leg.DayCountConvention.Should().BeNull();
            leg.ExchangesPrincipal.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("legs")]
    [InlineData("SwapLegs")]
    [InlineData("CASHFLOWLEGS")]
    public void VendorAliasesAndQuotedValues_PreserveTheirEconomics(string container)
    {
        var payload = new Dictionary<string, object?>
        {
            ["effectiveDate"] = "2026-01-15",
            ["maturityDate"] = "2031-01-15",
            [container] = new object[]
            {
                new
                {
                    NAME = "vendor-fixed",
                    RATETYPE = "Fixed",
                    currency = "USD",
                    PAYRECEIVE = "Payer",
                    COUPONRATE = "0.0425",
                    PRINCIPAL = "25000000",
                    FREQUENCY = "SemiAnnual",
                    DAYCOUNTBASIS = "30/360",
                    PRINCIPALEXCHANGE = "true"
                },
                new
                {
                    ID = "vendor-floating",
                    TYPE = "Floating",
                    currency = "USD",
                    SIDE = "Receiver",
                    REFERENCEINDEX = "SOFR",
                    SPREADBPS = "-10",
                    LASTFIXING = "0.0512",
                    FACEAMOUNT = "25000000",
                    FREQUENCY = "Quarterly",
                    DAYCOUNTCONVENTION = "ACT/360",
                    NOTIONALEXCHANGE = "false"
                }
            }
        };

        var terms = Resolve(RoundTripPayload(payload));
        terms.Legs.Should().HaveCount(2);
        var fixedLeg = terms.Legs![0];
        fixedLeg.LegId.Should().Be("vendor-fixed");
        fixedLeg.RateKind.Should().Be(CashFlowLegRateKind.Fixed);
        fixedLeg.Direction.Should().Be(CashFlowLegDirection.Pay);
        fixedLeg.FixedRate.Should().Be(0.0425m);
        fixedLeg.Notional.Should().Be(25_000_000m);
        fixedLeg.PaymentFrequency.Should().Be("SemiAnnual");
        fixedLeg.DayCountConvention.Should().Be("30/360");
        fixedLeg.ExchangesPrincipal.Should().BeTrue();

        var floatingLeg = terms.Legs[1];
        floatingLeg.LegId.Should().Be("vendor-floating");
        floatingLeg.RateKind.Should().Be(CashFlowLegRateKind.Floating);
        floatingLeg.Direction.Should().Be(CashFlowLegDirection.Receive);
        floatingLeg.IndexName.Should().Be("SOFR");
        floatingLeg.SpreadBps.Should().Be(-10m);
        floatingLeg.CurrentIndexRate.Should().Be(0.0512m);
        floatingLeg.Notional.Should().Be(25_000_000m);
        floatingLeg.PaymentFrequency.Should().Be("Quarterly");
        floatingLeg.DayCountConvention.Should().Be("ACT/360");
        floatingLeg.ExchangesPrincipal.Should().BeFalse();
    }

    [Fact]
    public void ConflictingDayCountAliases_PreserveTheReadersPreferredConvention()
    {
        var terms = Resolve(RoundTrip(new object[]
        {
            new
            {
                legType = "Fixed",
                currency = "USD",
                fixedRate = 0.04m,
                dayCountConvention = "ACT/360",
                dayCount = "30/360",
                dayCountBasis = "ACT/365"
            }
        }));

        terms.Legs.Should().ContainSingle().Which.DayCountConvention.Should().Be("ACT/360");
    }

    [Fact]
    public void ExplicitZeroRatesAndFalsePrincipalExchange_AreRetained()
    {
        var projection = RoundTrip(new object[]
        {
            new
            {
                legType = "Floating",
                currency = "USD",
                fixedRate = "0",
                spreadBps = "0",
                currentIndexRate = "0",
                notional = "1000000",
                exchangesPrincipal = "false"
            }
        });

        var leg = Resolve(projection).Legs.Should().ContainSingle().Which;
        leg.FixedRate.Should().Be(0m);
        leg.SpreadBps.Should().Be(0m);
        leg.CurrentIndexRate.Should().Be(0m);
        leg.Notional.Should().Be(1_000_000m);
        leg.ExchangesPrincipal.Should().BeFalse();
        projection.AssetSpecificTerms.GetProperty("legs")[0]
            .GetProperty("exchangesPrincipal").ValueKind.Should().Be(JsonValueKind.False);
    }

    [Theory]
    [InlineData("legId", "12")]
    [InlineData("direction", "true")]
    [InlineData("index", "[]")]
    [InlineData("fixedRate", "\"unknown\"")]
    [InlineData("spreadBps", "{}")]
    [InlineData("currentIndexRate", "\"missing\"")]
    [InlineData("notional", "\"25000000 USD\"")]
    [InlineData("paymentFrequency", "4")]
    [InlineData("dayCount", "{}")]
    [InlineData("exchangesPrincipal", "\"perhaps\"")]
    [InlineData("exchangesPrincipal", "1")]
    [InlineData("couponRate", "\"unknown\"")]
    [InlineData("lastFixing", "[]")]
    [InlineData("principalExchange", "\"perhaps\"")]
    public void MalformedOptionalTerms_AreRejectedInsteadOfDiscarded(string field, string rawValue)
    {
        using var document = JsonDocument.Parse(rawValue);
        var leg = new Dictionary<string, object?>
        {
            ["legType"] = "Fixed",
            ["currency"] = "USD",
            [field] = document.RootElement.Clone()
        };

        Action roundTrip = () => RoundTrip(new object[] { leg });

        roundTrip.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task PersistedSwap_ProjectsActualNotionalsAndNetsRetainedLegEconomics()
    {
        // First-of-month dates keep all four payments ahead of the real-time cutoff and
        // make the retained 30/360 conventions produce exact quarter/half-year amounts.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var effectiveDate = new DateOnly(today.Year, today.Month, 1);
        var persisted = RoundTripPayload(new
        {
            effectiveDate,
            maturityDate = effectiveDate.AddYears(1),
            legs = new object[]
            {
                new
                {
                    legId = "receive-fixed",
                    legType = "Fixed",
                    currency = "USD",
                    direction = "Receive",
                    fixedRate = 0.04m,
                    notional = 1_000_000m,
                    paymentFrequency = "SemiAnnual",
                    dayCount = "30/360",
                    exchangesPrincipal = false
                },
                new
                {
                    legId = "pay-floating",
                    legType = "Floating",
                    currency = "USD",
                    direction = "Pay",
                    index = "SOFR",
                    currentIndexRate = 0.03m,
                    spreadBps = 25m,
                    notional = 1_000_000m,
                    paymentFrequency = "Quarterly",
                    dayCount = "30/360",
                    exchangesPrincipal = false
                }
            }
        });
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(persisted.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                persisted.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(persisted.SecurityId, Arg.Any<CancellationToken>())
            .Returns(SecurityMasterMapping.ToDetail(persisted));
        var service = new SecurityMasterCashFlowService(
            store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(persisted.SecurityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.BlockedReason.Should().BeNull();
        projection.IsNormalizedPer100.Should().BeFalse();
        projection.LegSchedules.Should().HaveCount(2);
        projection.LegSchedules![0].Schedule.Should().HaveCount(2)
            .And.OnlyContain(static payment => payment.InterestAmount == 20_000m);
        projection.LegSchedules[1].Schedule.Should().HaveCount(4)
            .And.OnlyContain(static payment => payment.InterestAmount == 8_125m);
        projection.Schedule.Select(static payment => payment.InterestAmount)
            .Should().Equal(-8_125m, 11_875m, -8_125m, 11_875m);
        projection.Schedule.Should().OnlyContain(static payment => payment.PrincipalAmount == 0m);
    }

    private static StructuredCashFlowTerms Resolve(SecurityProjectionRecord projection)
        => StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));

    private static SecurityProjectionRecord RoundTrip(object[] legs)
        => RoundTripPayload(new
        {
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs
        });

    private static SecurityProjectionRecord RoundTripPayload(object payload)
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var projection = new SecurityProjectionRecord(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Swap",
            Status: SecurityStatusDto.Active,
            DisplayName: "Swap codec test",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "SWAP-CODEC-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Swap codec test", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(payload),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "swap-round-trip-tests",
                asOf,
                updatedBy = "swap-round-trip-tests"
            }),
            Version: 1,
            EffectiveFrom: asOf,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SWAP-CODEC-1", true, asOf)],
            Aliases: []);

        return SecurityMasterMapping.ToProjection(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)));
    }
}
