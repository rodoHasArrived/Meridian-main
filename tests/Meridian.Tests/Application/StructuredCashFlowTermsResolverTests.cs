using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application;

public sealed class StructuredCashFlowTermsResolverTests
{
    [Fact]
    public void Resolve_ShouldReadTypedTermsAndTypedFactorSchedule()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            maturityDate = "2030-06-30",
            issueDate = "2020-06-30",
            originalFace = 250m,
            currentFactor = 0.95m,
            couponRate = 4.5m,
            paymentFrequency = "SemiAnnual",
            dayCountConvention = "30/360",
            factorSchedule = new object[]
            {
                new { asOfDate = "2020-01-01", factor = 0.9m },
                new { factorDate = "2021-01-01", currentFactor = 0.8m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.MaturityDate.Should().Be(new DateOnly(2030, 6, 30));
        terms.IssueDate.Should().Be(new DateOnly(2020, 6, 30));
        terms.PrincipalFace.Should().Be(250m);
        terms.CurrentFactor.Should().Be(0.95m);
        terms.CouponRate.Should().Be(4.5m);
        terms.PaymentFrequency.Should().Be("SemiAnnual");
        terms.DayCountConvention.Should().Be("30/360");
        terms.HasFactorSchedule.Should().BeTrue();
        terms.FactorSchedule.Should().SatisfyRespectively(
            first => { first.AsOfDate.Should().Be(new DateOnly(2020, 1, 1)); first.Factor.Should().Be(0.9m); },
            second => { second.AsOfDate.Should().Be(new DateOnly(2021, 1, 1)); second.Factor.Should().Be(0.8m); });
    }

    [Fact]
    public void Resolve_EmptyGovernedFactorSchedule_IsAuthoritativeOverOuterRows()
    {
        // The governed nested schedule's PRESENCE claims ownership: profileFields carrying an
        // EMPTY factorScheduleEntries deliberately asserts "no factor history", so the resolver
        // must not fall through to the ungoverned outer pass-through rows.
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            profileFields = new
            {
                factorScheduleEntries = Array.Empty<object>()
            },
            factorScheduleEntries = new object[]
            {
                new { asOfDate = "2021-01-01", factor = 0.8m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.HasFactorSchedule.Should().BeFalse(
            "governance explicitly left the nested schedule empty, so the outer rows must not supply pass-through economics");
    }

    [Fact]
    public void Resolve_ShouldReadAndAggregateContractualPrincipalSchedule()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-30", amount = 25m },
                new { paymentDate = "2027-06-30", amount = 30m },
                new { paymentDate = "2027-06-30", amount = 10m },
                new { paymentDate = "2029-06-30", amount = -1m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.HasPrincipalSchedule.Should().BeTrue();
        terms.PrincipalSchedule.Should().SatisfyRespectively(
            first =>
            {
                first.PaymentDate.Should().Be(new DateOnly(2027, 6, 30));
                first.Amount.Should().Be(40m);
            },
            second =>
            {
                second.PaymentDate.Should().Be(new DateOnly(2028, 6, 30));
                second.Amount.Should().Be(25m);
            });
    }

    [Fact]
    public void Resolve_ShouldPreferTypedFactorScheduleEntriesOverFreeTextFactorSchedule()
    {
        // The canonical F# StructuredCredit serializer emits a free-text factorSchedule (legacy
        // trustee-report pointer) alongside the typed factorScheduleEntries array. The resolver
        // must skip the non-array string and seed FactorAsOf from the typed entries.
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            tranche = "B",
            originalFace = 10_000_000m,
            currentFactor = 0.8235m,
            factorSchedule = "See trustee report 2026-07",
            factorScheduleEntries = new object[]
            {
                new { asOfDate = "2026-06-01", factor = 0.8412m },
                new { asOfDate = "2026-07-01", factor = 0.8235m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.HasFactorSchedule.Should().BeTrue(
            "the typed factorScheduleEntries array must resolve even when the legacy free-text factorSchedule is present");
        terms.FactorAsOf(new DateOnly(2026, 6, 15)).Should().Be(0.8412m);
        terms.FactorAsOf(new DateOnly(2026, 7, 15)).Should().Be(0.8235m);
        terms.FactorAsOf(new DateOnly(2026, 5, 1)).Should().Be(0.8235m,
            "before the first scheduled point the scalar currentFactor is the fallback");
    }

    [Fact]
    public void Resolve_ShouldReadTypedCashFlowLegs()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            maturityDate = "2030-06-30",
            paymentFrequency = "SemiAnnual",
            dayCountConvention = "30/360",
            legs = new object[]
            {
                new { legType = "Fixed", direction = "Receive", fixedRate = 0.035m, notional = 1000m },
                new { legType = "Float", side = "Pay", index = "SOFR", spreadBps = 25m, currentIndexRate = 0.031m, exchangesPrincipal = true }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.HasLegs.Should().BeTrue();
        terms.Legs.Should().SatisfyRespectively(
            fixedLeg =>
            {
                fixedLeg.LegId.Should().Be("leg-1");
                fixedLeg.RateKind.Should().Be(CashFlowLegRateKind.Fixed);
                fixedLeg.Direction.Should().Be(CashFlowLegDirection.Receive);
                fixedLeg.FixedRate.Should().Be(0.035m);
                fixedLeg.Notional.Should().Be(1000m);
                fixedLeg.ExchangesPrincipal.Should().BeFalse();
            },
            floatLeg =>
            {
                floatLeg.LegId.Should().Be("leg-2");
                floatLeg.RateKind.Should().Be(CashFlowLegRateKind.Floating);
                floatLeg.Direction.Should().Be(CashFlowLegDirection.Pay);
                floatLeg.IndexName.Should().Be("SOFR");
                floatLeg.SpreadBps.Should().Be(25m);
                floatLeg.CurrentIndexRate.Should().Be(0.031m);
                floatLeg.ExchangesPrincipal.Should().BeTrue();
            });
    }

    [Fact]
    public void Resolve_PersistedSwapLegs_WithoutDirections_LeaveDirectionNull()
    {
        // Exactly the four fields the F# SwapLeg serializer persists: no direction information,
        // so the resolver must not invent one.
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            maturityDate = "2030-06-30",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.041m },
                new { legType = "Float", currency = "USD", index = "SOFR" }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.Legs.Should().HaveCount(2);
        terms.Legs![0].Direction.Should().BeNull();
        terms.Legs[0].RateKind.Should().Be(CashFlowLegRateKind.Fixed);
        terms.Legs[1].Direction.Should().BeNull();
        terms.Legs[1].RateKind.Should().Be(CashFlowLegRateKind.Floating);
    }

    [Fact]
    public void FactorAsOf_ShouldPickLatestScheduledFactorAndFallBackToScalar()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            currentFactor = 0.95m,
            factorSchedule = new object[]
            {
                new { asOfDate = "2020-01-01", factor = 0.9m },
                new { asOfDate = "2021-01-01", factor = 0.8m }
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.FactorAsOf(new DateOnly(2020, 6, 1)).Should().Be(0.9m);
        terms.FactorAsOf(new DateOnly(2022, 1, 1)).Should().Be(0.8m);
        // Before any schedule point, fall back to the scalar current factor.
        terms.FactorAsOf(new DateOnly(2019, 1, 1)).Should().Be(0.95m);
    }

    [Fact]
    public void Resolve_ShouldHonorAliasPriority_ParBeatsOriginalFace()
    {
        var security = Build(JsonSerializer.SerializeToElement(new { par = 100m, originalFace = 200m }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.PrincipalFace.Should().Be(100m);
    }

    [Fact]
    public void Resolve_ShouldReadFromNestedProfileFields()
    {
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            profileFields = new { couponRate = 3.25m }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.CouponRate.Should().Be(3.25m);
    }

    [Fact]
    public void Resolve_ShouldPreferGovernedProfileFieldsOverOuterDuplicates()
    {
        // profileFields values are schema- and profile-validated on write; extra outer keys on an
        // envelope are ungoverned pass-through. An unvalidated outer maturity must not shadow the
        // governed one in projections and conflict comparisons.
        var security = Build(JsonSerializer.SerializeToElement(new
        {
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            maturity = "2040-01-01",
            couponRate = 9.99m,
            profileFields = new
            {
                maturity = "2030-01-01",
                couponRate = 4.5m
            }
        }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.MaturityDate.Should().Be(new DateOnly(2030, 1, 1),
            "the governed profile field outranks the ungoverned outer duplicate");
        terms.CouponRate.Should().Be(4.5m);
    }

    [Fact]
    public void Resolve_WithNoStructuredTerms_ShouldReturnNullsAndEmptySchedule()
    {
        var security = Build(JsonSerializer.SerializeToElement(new { }));

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.PrincipalFace.Should().BeNull();
        terms.CouponRate.Should().BeNull();
        terms.MaturityDate.Should().BeNull();
        terms.HasFactorSchedule.Should().BeFalse();
        terms.FactorSchedule.Should().BeEmpty();
        terms.HasPrincipalSchedule.Should().BeFalse();
        terms.PrincipalSchedule.Should().BeEmpty();
    }

    private static SecurityDetailDto Build(JsonElement assetSpecificTerms)
        => new(
            Guid.NewGuid(),
            "Bond",
            SecurityStatusDto.Active,
            "Test bond",
            "USD",
            JsonSerializer.SerializeToElement(new { }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.UtcNow)],
            [],
            1,
            DateTimeOffset.UtcNow,
            null);
}
