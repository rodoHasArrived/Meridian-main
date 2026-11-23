using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed partial class PostgresDirectLendingCommandServiceTests
{
    [Fact]
    public async Task AcceptedLifecycleCommands_RebuildFromEmittedPayloadsWithExactRevisionEvidence()
    {
        var loanId = Guid.Parse("81111111-2222-4222-8222-222222222222");
        var date = new DateOnly(2026, 3, 24);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var template = BuildContract(loanId);
        var history = new List<LoanEventLineageDto>();
        void Seed(string type, object payload, DateOnly effectiveDate)
        {
            var version = history.Count + 1;
            history.Add(new LoanEventLineageDto(Guid.NewGuid(), version, type, 1, effectiveDate,
                new DateTimeOffset(2026, 3, 22, 12, version, 0, TimeSpan.Zero),
                JsonSerializer.Serialize(payload, json), null, null, null, "synthetic-lifecycle", false));
        }
        Seed("loan.created", new
        {
            loanId, template.FacilityName, template.Borrower, template.EffectiveDate, terms = template.CurrentTerms
        }, template.EffectiveDate);
        Seed("loan.activated", new { activationDate = template.EffectiveDate }, template.EffectiveDate);
        Seed("loan.drawdown-booked", new
        {
            lotId = Guid.Parse("87777777-2222-4222-8222-222222222222"), amount = 100m,
            tradeDate = template.EffectiveDate, settleDate = template.EffectiveDate
        }, template.EffectiveDate);
        Seed("loan.drawdown-booked", new
        {
            lotId = Guid.Parse("88888888-2222-4222-8222-222222222222"), amount = 50m,
            tradeDate = date.AddDays(-1), settleDate = date.AddDays(-1)
        }, date.AddDays(-1));
        var rebuilder = new DirectLendingEventRebuilder();
        var current = rebuilder.Rebuild(loanId, history)!;
        var capturedJournalWrites = new List<LedgerJournalEntryWrite>();
        var stateStore = Substitute.For<IDirectLendingStateStore>();
        stateStore.SaveAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<LoanContractDetailDto>(), Arg.Any<LoanServicingStateDto>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<JsonDocument>(),
                Arg.Any<DirectLendingEventWriteMetadata>(), Arg.Any<DirectLendingPersistenceBatch?>(),
                Arg.Any<IReadOnlyList<LedgerJournalEntryWrite>?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                call.ArgAt<long>(1).Should().Be(current.AggregateVersion);
                var metadata = call.ArgAt<DirectLendingEventWriteMetadata>(9);
                var retained = new LoanEventLineageDto(call.ArgAt<Guid>(12), call.ArgAt<long>(2),
                    call.ArgAt<string>(5), call.ArgAt<int>(6), call.ArgAt<DateOnly?>(7),
                    DateTimeOffset.UtcNow.AddMinutes(5), call.ArgAt<JsonDocument>(8).RootElement.GetRawText(),
                    metadata.CausationId, metadata.CorrelationId, metadata.CommandId, metadata.SourceSystem, metadata.ReplayFlag);
                history.Add(retained);
                current = new PersistedDirectLendingState(loanId, retained.AggregateVersion,
                    call.ArgAt<LoanContractDetailDto>(3), call.ArgAt<LoanServicingStateDto>(4));
                capturedJournalWrites.AddRange(call.ArgAt<IReadOnlyList<LedgerJournalEntryWrite>?>(11) ?? []);
            });
        var query = Substitute.For<IDirectLendingQueryService>();
        query.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>()).Returns(_ => current);
        query.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns(_ => history.ToArray());
        var service = new PostgresDirectLendingCommandService(stateStore,
            Substitute.For<IDirectLendingOperationsStore>(), query,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
            new DirectLendingOptions());

        void AssertRecovery()
        {
            var writesBefore = capturedJournalWrites.Count;
            var retainedBytes = JsonSerializer.Serialize(history, json);
            var recovered = rebuilder.Rebuild(loanId, history)!;
            JsonSerializer.Serialize(recovered, json).Should().Be(JsonSerializer.Serialize(current, json));
            JsonSerializer.Serialize(history, json).Should().Be(retainedBytes);
            capturedJournalWrites.Should().HaveCount(writesBefore, "aggregate replay cannot publish new journals");
            current.Servicing.RevisionHistory[0].CreatedAt.Should().NotBe(history[^1].RecordedAt);
        }

        (await service.AssessFeeAsync(loanId, new AssessFeeRequest("Administration", 25m, date, "Synthetic fee")))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        var added = await service.AddCollateralAsync(loanId,
            new AddCollateralRequest(CollateralType.Equipment, "Synthetic machine", 200m, CurrencyCode.USD, date));
        added.IsSuccess.Should().BeTrue();
        var collateralId = current.Servicing.Collateral!.Single().CollateralId;
        AssertRecovery();
        (await service.UpdateCollateralValueAsync(loanId, new UpdateCollateralValueRequest(collateralId, 175m, date)))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.TransitionLoanStatusAsync(loanId,
            new TransitionLoanStatusRequest(LoanStatus.Defaulted, "Synthetic default", date))).IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.ApplyWriteOffAsync(loanId, new ApplyWriteOffRequest(25m, date, "Synthetic partial loss")))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.CreateServicerReportBatchAsync(new CreateServicerReportBatchRequest(
            "Synthetic servicer", "Position", "JSON", date, null, null, null,
            [new ServicerPositionReportLineImportDto(loanId, 125m, 2m, null, 1m, null, null, null, null, "{}")], [])))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.TransitionLoanStatusAsync(loanId,
            new TransitionLoanStatusRequest(LoanStatus.Active, "Synthetic cure", date))).IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.TogglePikAsync(loanId, new TogglePikRequest(true, date, "Synthetic election")))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();

        // An existing state can carry acquisition basis not present in older event schemas.
        // The newly accepted amortization must retain that actual pre/post basis, not infer it.
        current = current with { Servicing = current.Servicing with { UnamortizedDiscount = 100m, UnamortizedPremium = 20m } };
        (await service.AmortizeDiscountPremiumAsync(loanId, new AmortizeDiscountPremiumRequest(date)))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.AmortizeDiscountPremiumAsync(loanId, new AmortizeDiscountPremiumRequest(date.AddDays(1))))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();
        (await service.RemoveCollateralAsync(loanId, new RemoveCollateralRequest(collateralId, "Synthetic release")))
            .IsSuccess.Should().BeTrue();
        AssertRecovery();

        current.Servicing.Balances.PrincipalOutstanding.Should().Be(125m);
        current.Servicing.DrawdownLots.Select(lot => lot.RemainingPrincipal).Should().Equal(75m, 50m);
        current.Servicing.Collateral.Should().BeEmpty();
        history.Skip(4).Select(item => item.EventType).Should().Contain(new[]
        {
            "loan.fee-assessed", "loan.collateral-added", "loan.collateral-value-updated", "loan.status-transitioned",
            "loan.write-off-applied", "loan.servicer-position-imported", "loan.pik-toggled",
            "loan.discount-premium-amortized", "loan.collateral-removed"
        });
    }
}
