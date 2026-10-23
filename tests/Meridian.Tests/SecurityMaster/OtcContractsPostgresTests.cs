using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The OTC contract model in PostgreSQL: a master agreement, a collateral agreement under it, a
/// netting set that joins them, and contracts on a Security Master instrument that live in the
/// netting set. Contracts are refused on instruments the catalog does not trade bilaterally, and
/// closed contracts are history.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OtcContractsPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateOnly Trade = new(2026, 3, 2);
    private readonly SecurityMasterDatabaseFixture _fixture;

    public OtcContractsPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task Agreements_NettingSets_AndContracts_RoundTrip()
    {
        var service = Build();
        var store = new PostgresOtcContractStore(_fixture.Options);
        var swapId = Guid.NewGuid();
        await CreateAsync(service, swapId, "Swap", new
        {
            effectiveDate = "2026-03-04",
            maturityDate = "2031-03-04",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.035m, exchangesPrincipal = false },
                new { legType = "Floating", currency = "USD", floatingIndex = "SOFR", exchangesPrincipal = false }
            }
        });

        var master = await store.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(
            null, "Big Bank", "5493001KJTIIGC8Y1R12", "Meridian Master Fund", null, OtcMasterAgreementType.Isda2002, new DateOnly(2024, 1, 15), "New York", null, "casey"));
        master.Version.Should().Be(1);
        (await store.GetMasterAgreementAsync(master.AgreementId)).Should().BeEquivalentTo(master, o => o.Excluding(m => m.UpdatedAt));
        (await store.ListMasterAgreementsAsync("big bank")).Should().ContainSingle(m => m.AgreementId == master.AgreementId);

        var csa = await store.UpsertCollateralAgreementAsync(new UpsertOtcCollateralAgreementRequest(
            null, master.AgreementId, "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 2m)],
            new DateOnly(2024, 1, 15), null, "standard CSA", "casey"));
        var csaRead = await store.GetCollateralAgreementAsync(csa.CollateralAgreementId);
        csaRead.Should().NotBeNull();
        csaRead!.EligibleCollateral.Should().HaveCount(2);
        csaRead.EligibleCollateral[1].HaircutPercent.Should().Be(2m);
        csaRead.IndependentAmount.Should().Be(1_000_000m);

        var nettingSet = await store.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, master.AgreementId, csa.CollateralAgreementId, "Big Bank ISDA", null, "casey"));

        var first = await store.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, nettingSet.NettingSetId, "BB-0001", OtcContractDirection.Buy, 10_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Pending, null, null, "casey"));
        var second = await store.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, nettingSet.NettingSetId, "BB-0002", OtcContractDirection.Sell, 4_000_000m, "USD", Trade.AddDays(1), Trade.AddDays(3), Trade.AddYears(5), OtcContractStatus.Confirmed, "CONF-2", null, "casey"));

        var onInstrument = await store.ListContractsForSecurityAsync(swapId);
        onInstrument.Select(c => c.ContractId).Should().Equal(new[] { first.ContractId, second.ContractId }, "two contracts share one instrument, ordered by trade date");

        var view = await store.GetNettingSetAsync(nettingSet.NettingSetId);
        view.Should().NotBeNull();
        view!.MasterAgreement.AgreementId.Should().Be(master.AgreementId);
        view.CollateralAgreement!.CollateralAgreementId.Should().Be(csa.CollateralAgreementId);
        view.Contracts.Should().HaveCount(2);

        // Confirming the first contract amends it in place and bumps its version.
        var confirmed = await store.UpsertContractAsync(new UpsertOtcContractRequest(
            first.ContractId, swapId, nettingSet.NettingSetId, "BB-0001", OtcContractDirection.Buy, 10_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, "CONF-1", null, "casey"));
        confirmed.Version.Should().Be(2);
        (await store.GetContractAsync(first.ContractId))!.ConfirmationReference.Should().Be("CONF-1");

        // Terminating it closes it: a further amendment is refused.
        await store.UpsertContractAsync(new UpsertOtcContractRequest(
            first.ContractId, swapId, nettingSet.NettingSetId, "BB-0001", OtcContractDirection.Buy, 10_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(1), OtcContractStatus.Terminated, "CONF-1", "early termination", "casey"));
        await store.Invoking(s => s.UpsertContractAsync(new UpsertOtcContractRequest(
                first.ContractId, swapId, nettingSet.NettingSetId, "BB-0001", OtcContractDirection.Buy, 12_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Terminated*");
    }

    [SecurityMasterDatabaseFact]
    public async Task Contracts_AreRefused_OnListedInstruments_UnknownHomes_AndCrossMasterCollateral()
    {
        var service = Build();
        var store = new PostgresOtcContractStore(_fixture.Options);
        var equityId = Guid.NewGuid();
        var cdsId = Guid.NewGuid();
        await CreateAsync(service, equityId, "Equity", new { shareClass = "Common" });
        await CreateAsync(service, cdsId, "CreditDefaultSwap", new
        {
            referenceEntity = "Acme Corp",
            protectionSide = "Buy",
            notional = 5_000_000m,
            fixedCouponBps = 100m,
            effectiveDate = "2026-03-04",
            maturityDate = "2031-06-20"
        });

        var masterA = await store.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(null, "Bank A", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade));
        var masterB = await store.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(null, "Bank B", null, "Fund", null, OtcMasterAgreementType.Isda1992, Trade));
        var csaB = await store.UpsertCollateralAgreementAsync(new UpsertOtcCollateralAgreementRequest(null, masterB.AgreementId, "USD", 0m, 0m, 0m, null, null, "Daily", [], Trade));
        var nettingSetA = await store.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, masterA.AgreementId, null, "Bank A"));

        UpsertOtcContractRequest Contract(Guid securityId, Guid nettingSetId) =>
            new(null, securityId, nettingSetId, null, OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade, null, OtcContractStatus.Pending);

        await store.Invoking(s => s.UpsertContractAsync(Contract(equityId, nettingSetA.NettingSetId)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*'Equity' is not traded as a bilateral contract*");
        await store.Invoking(s => s.UpsertContractAsync(Contract(Guid.NewGuid(), nettingSetA.NettingSetId)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*not in the Security Master*");
        await store.Invoking(s => s.UpsertContractAsync(Contract(cdsId, Guid.NewGuid())))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*netting set*does not exist*");
        await store.Invoking(s => s.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, masterA.AgreementId, csaB.CollateralAgreementId, "Mixed")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*same master agreement*");
        await store.Invoking(s => s.UpsertCollateralAgreementAsync(new UpsertOtcCollateralAgreementRequest(null, Guid.NewGuid(), "USD", 0m, 0m, 0m, null, null, "Daily", [], Trade)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*master agreement*does not exist*");
        await store.Invoking(s => s.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(Guid.NewGuid(), "Bank C", null, "Fund", null, OtcMasterAgreementType.Other, Trade)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*does not exist*");

        var contract = await store.UpsertContractAsync(Contract(cdsId, nettingSetA.NettingSetId));
        contract.Status.Should().Be(OtcContractStatus.Pending);
        (await store.GetNettingSetAsync(nettingSetA.NettingSetId))!.CollateralAgreement.Should().BeNull();
    }

    private SecurityMasterService Build()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
    }

    private static async Task CreateAsync(SecurityMasterService service, Guid securityId, string assetClass, object terms)
    {
        var validFrom = DateTimeOffset.UtcNow.AddDays(-30);
        var code = "OTC" + securityId.ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            assetClass,
            JsonSerializer.SerializeToElement(new
            {
                displayName = code,
                currency = "USD",
                countryOfRisk = "US",
                issuerName = code,
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(terms),
            new[] { new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, code, true, validFrom, null, null) },
            validFrom,
            "test",
            "codex",
            null,
            "create for the OTC contract model"));
    }
}
