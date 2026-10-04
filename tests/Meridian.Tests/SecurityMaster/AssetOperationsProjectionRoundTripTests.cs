using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// End-to-end guards for the schema-driven Asset Operations projections against a real database:
/// the generated upsert and child inserts have to agree with the migration 033 DDL, and the
/// projection has to be cleared — children included — when a record stops qualifying for it. Those
/// two properties are exactly what a pure decode test cannot prove.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssetOperationsProjectionRoundTripTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public AssetOperationsProjectionRoundTripTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoan_ProjectsTermsCovenantsAndPrincipalScheduleAndClearsThemOnAClassChange()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var projectionStore = new PostgresDirectLoanReferenceProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            maturity = "2030-03-31",
            referenceIndex = "SOFR",
            spreadBps = 425m,
            currentCouponRate = 9.55m,
            resetFrequency = "Quarterly",
            pricingSource = "IHSMarkit",
            covenants = new[]
            {
                new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "Tested quarterly" },
                new { covenantType = "MinFixedCharge", threshold = "1.10x", notes = (string?)null }
            },
            principalSchedule = new[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2028-03-31", amount = 1_500_000m }
            }
        }));

        var loan = await projectionStore.GetDirectLoanAsync(securityId);
        loan.Should().NotBeNull();
        loan!.Borrower.Should().Be("Meridian Industrials LLC");
        loan.MaturityDate.Should().Be(new DateOnly(2030, 3, 31));
        loan.ReferenceIndex.Should().Be("SOFR");
        loan.SpreadBps.Should().Be(425m);
        loan.CurrentCouponRate.Should().Be(9.55m);

        (await projectionStore.GetByBorrowerAsync("  meridian industrials llc "))
            .Should().ContainSingle().Which.SecurityId.Should().Be(securityId);

        var covenants = await projectionStore.GetCovenantsAsync(securityId);
        // Ordinal preserves the order the terms document declares.
        covenants.Select(covenant => covenant.CovenantType)
            .Should().Equal("MaxLeverage", "MinFixedCharge");
        covenants.Select(covenant => covenant.Ordinal).Should().Equal(0, 1);
        covenants[0].Threshold.Should().Be("4.5x");
        covenants[1].Notes.Should().BeNull();

        var schedule = await projectionStore.GetPrincipalScheduleAsync(securityId);
        schedule.Select(entry => entry.Amount).Should().Equal(1_250_000m, 1_500_000m);

        (await projectionStore.GetPrincipalPaymentsDueAsync(new(2028, 1, 1), new(2028, 12, 31)))
            .Should().ContainSingle().Which.PaymentDate.Should().Be(new DateOnly(2028, 3, 31));

        // A shortened schedule must not leave the dropped instalment behind.
        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 2, new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            covenants = Array.Empty<object>(),
            principalSchedule = new[] { new { paymentDate = "2027-03-31", amount = 1_250_000m } }
        }));

        (await projectionStore.GetCovenantsAsync(securityId)).Should().BeEmpty();
        (await projectionStore.GetPrincipalScheduleAsync(securityId)).Should().ContainSingle();
        (await projectionStore.GetDirectLoanAsync(securityId))!.MaturityDate
            .Should().BeNull("an amendment that drops an optional term must clear its column");

        // Every writer runs for every record, so a class change clears the loan projection outright.
        await store.UpsertProjectionAsync(Record(securityId, "Equity", version: 3, new { schemaVersion = 1, shareClass = "Common" }));

        (await projectionStore.GetDirectLoanAsync(securityId)).Should().BeNull();
        (await projectionStore.GetPrincipalScheduleAsync(securityId)).Should().BeEmpty();
        (await projectionStore.GetCovenantsAsync(securityId)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task StructuredCredit_ProjectsTrancheTermsAndResolvesTheFactorEffectiveOnADate()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var projectionStore = new PostgresStructuredCreditReferenceProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "StructuredCredit", version: 1, new
        {
            schemaVersion = 1,
            tranche = "B",
            poolId = "MRDN-2026-1",
            collateralType = "CLO",
            originalFace = 10_000_000m,
            currentFactor = 0.8235m,
            couponOrIndex = "SOFR+250",
            factorSchedule = "See trustee report 2026-07",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-06-01", factor = 0.8412m },
                new { asOfDate = "2026-07-01", factor = 0.8235m }
            },
            maturity = "2031-06-15"
        }));

        var tranche = await projectionStore.GetStructuredCreditAsync(securityId);
        tranche.Should().NotBeNull();
        tranche!.Tranche.Should().Be("B");
        tranche.PoolId.Should().Be("MRDN-2026-1");
        tranche.OriginalFace.Should().Be(10_000_000m);
        tranche.CurrentFactor.Should().Be(0.8235m);
        tranche.FactorScheduleReference.Should().Be("See trustee report 2026-07");
        tranche.MaturityDate.Should().Be(new DateOnly(2031, 6, 15));

        (await projectionStore.GetByPoolAsync("mrdn-2026-1")).Should().ContainSingle();
        (await projectionStore.GetByCollateralTypeAsync("CLO")).Should().ContainSingle();

        var schedule = await projectionStore.GetFactorScheduleAsync(securityId);
        schedule.Select(point => point.Factor).Should().Equal(0.8412m, 0.8235m);

        // The relational FactorAsOf lookup: the latest point on or before the date, nothing after it.
        (await projectionStore.GetFactorAsOfAsync(securityId, new(2026, 6, 15)))!.Factor.Should().Be(0.8412m);
        (await projectionStore.GetFactorAsOfAsync(securityId, new(2026, 7, 1)))!.Factor.Should().Be(0.8235m);
        (await projectionStore.GetFactorAsOfAsync(securityId, new(2026, 5, 31)))
            .Should().BeNull("the schedule starts after that date");
    }

    [SecurityMasterDatabaseFact]
    public async Task StructuredCredit_ProjectsAProfileBackedTrancheFromItsProfileEnvelope()
    {
        // A profile-backed record's asset terms ARE the envelope, so the tranche's economics sit
        // under profileFields. Reading only the document root would leave every governed
        // profile-routed tranche unprojected while the accounting adapter reads it fine.
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var projectionStore = new PostgresStructuredCreditReferenceProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "StructuredCredit", version: 1, new
        {
            schemaVersion = 3,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            profileFields = new
            {
                tranche = "A-1",
                poolId = "POOL-1",
                collateralType = "CLO",
                originalFace = 1_000_000m,
                currentFactor = 0.5m,
                couponOrIndex = "SOFR+250",
                factorScheduleEntries = new[]
                {
                    new { asOfDate = "2026-01-01", factor = 0.8m }
                },
                maturity = "2032-01-01"
            }
        }));

        var tranche = await projectionStore.GetStructuredCreditAsync(securityId);
        tranche.Should().NotBeNull();
        tranche!.Tranche.Should().Be("A-1");
        tranche.PoolId.Should().Be("POOL-1");
        tranche.OriginalFace.Should().Be(1_000_000m);

        (await projectionStore.GetFactorAsOfAsync(securityId, new(2026, 6, 30)))!.Factor.Should().Be(0.8m);
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoan_ProjectsACanonicalPayloadWhoseOptionalTermsAreExplicitNull()
    {
        // The canonical serializer writes every declared key and nulls the ones the record does not
        // carry, so this is the shape production persists for a loan with no spread or maturity.
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var projectionStore = new PostgresDirectLoanReferenceProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, new
        {
            schemaVersion = 1,
            borrower = "Sparse Borrower LLC",
            covenants = Array.Empty<object>(),
            currentCouponRate = (decimal?)null,
            maturity = (string?)null,
            pricingSource = (string?)null,
            principalSchedule = Array.Empty<object>(),
            referenceIndex = (string?)null,
            resetFrequency = (string?)null,
            spreadBps = (decimal?)null
        }));

        var loan = await projectionStore.GetDirectLoanAsync(securityId);
        loan.Should().NotBeNull();
        loan!.Borrower.Should().Be("Sparse Borrower LLC");
        loan.SpreadBps.Should().BeNull();
        loan.MaturityDate.Should().BeNull();
        loan.ReferenceIndex.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task StructuredCredit_PublishesNoProjectionWhenARequiredTermIsMissing()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var projectionStore = new PostgresStructuredCreditReferenceProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "StructuredCredit", version: 1, new
        {
            schemaVersion = 1,
            tranche = "B",
            collateralType = "CLO",
            couponOrIndex = "SOFR+250"
            // originalFace absent: no projection rather than a row with an invented face.
        }));

        (await projectionStore.GetStructuredCreditAsync(securityId)).Should().BeNull();
        (await projectionStore.GetFactorScheduleAsync(securityId)).Should().BeEmpty();
    }

    private static SecurityProjectionRecord Record(Guid securityId, string assetClass, long version, object assetSpecificTerms)
    {
        var identifierValue = $"AOPS-{securityId:N}";
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var identifier = new SecurityIdentifierDto(
            SecurityIdentifierKind.InternalCode,
            identifierValue,
            true,
            effectiveFrom);

        return new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            $"Asset operations projection fixture {securityId:N}",
            "USD",
            SecurityIdentifierKind.InternalCode.ToString(),
            identifierValue,
            JsonSerializer.SerializeToElement(new
            {
                displayName = $"Asset operations projection fixture {securityId:N}",
                currency = "USD"
            }),
            JsonSerializer.SerializeToElement(assetSpecificTerms),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "integration-test",
                updatedBy = "asset-operations-projection-fixture"
            }),
            version,
            effectiveFrom,
            null,
            [identifier],
            []);
    }
}
