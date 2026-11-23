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
    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(25, 75)]
    [InlineData(100, 0)]
    public async Task RestructureLoanAsync_UsesOneAppliedHaircutForStateLotsEventAndJournal(int? haircutValue, decimal remaining)
    {
        decimal? haircut = haircutValue;
        var fixture = new RestructuringCommandFixture();
        var before = fixture.State;
        var request = fixture.Request(haircut);
        var result = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);

        result.IsSuccess.Should().BeTrue();
        fixture.State.Contract.Status.Should().Be(LoanStatus.Workout);
        fixture.State.Servicing.Status.Should().Be(LoanStatus.Workout);
        fixture.State.Servicing.Balances.PrincipalOutstanding.Should().Be(remaining);
        fixture.State.Servicing.DrawdownLots.Sum(lot => lot.RemainingPrincipal).Should().Be(remaining);
        fixture.State.Servicing.DrawdownLots.Select(lot => lot.OriginalPrincipal).Should().Equal(60m, 40m);
        fixture.State.Servicing.TotalDrawn.Should().Be(before.Servicing.TotalDrawn);
        fixture.State.Servicing.AvailableToDraw.Should().Be(before.Servicing.AvailableToDraw);
        fixture.State.Servicing.ServicingRevision.Should().Be(before.Servicing.ServicingRevision + 1);
        var retained = fixture.History.Should().ContainSingle().Subject;
        retained.CommandId.Should().Be(fixture.Metadata.CommandId);
        retained.EffectiveDate.Should().Be(request.EffectiveDate);
        fixture.OutboxMessages.Should().HaveCount(3);
        fixture.OutboxMessages.Should().OnlyContain(message => message.MessageKey == $"{fixture.LoanId:D}/{retained.EventId:D}");
        using var payload = JsonDocument.Parse(retained.PayloadJson);
        foreach (var name in new[] { "haircutAmount", "principalHaircut", "appliedPrincipalHaircut" })
            payload.RootElement.GetProperty(name).GetDecimal().Should().Be(haircut ?? 0m);
        payload.RootElement.GetProperty("servicingRevision").Deserialize<ServicingRevisionDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().Be(fixture.State.Servicing.RevisionHistory[0]);

        if (haircut.GetValueOrDefault() == 0m)
        {
            fixture.Writes.Should().BeEmpty();
        }
        else
        {
            var journal = fixture.Writes.Should().ContainSingle().Subject.Entry;
            journal.IsBalanced.Should().BeTrue();
            journal.Lines.Single(line => line.Account.Name == "LoanPrincipal").Credit.Should().Be(100m - remaining);
            journal.Lines.Single(line => line.Account.Name == "RestructuringLoss").Debit.Should().Be(100m - remaining);
            journal.Lines.Should().OnlyContain(line => line.Currency != null && line.Currency.TransactionCurrency == "USD");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(150)]
    public async Task RestructureLoanAsync_InvalidHaircutDoesNotWriteOrChangeTerms(decimal haircut)
    {
        var fixture = new RestructuringCommandFixture();
        var before = fixture.State;
        var request = fixture.Request(haircut) with { NewTerms = before.Contract.CurrentTerms with { FixedAnnualRate = 0.04m } };

        var result = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);

        result.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        fixture.State.Should().Be(before);
        fixture.SaveCount.Should().Be(0);
        fixture.History.Should().BeEmpty();
        fixture.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task RestructureLoanAsync_InvalidReplacementTermsDoesNotApplyHaircut()
    {
        var fixture = new RestructuringCommandFixture();
        var before = fixture.State;
        var request = fixture.Request(25m) with { NewTerms = before.Contract.CurrentTerms with { CommitmentAmount = 0m } };

        var result = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);

        result.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        fixture.State.Should().Be(before);
        fixture.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task RestructureLoanAsync_FullHaircutRetryAfterRestartRetainsOneEventTermsVersionAndJournal()
    {
        var fixture = new RestructuringCommandFixture();
        var request = fixture.Request(100m) with { NewTerms = fixture.State.Contract.CurrentTerms with { FixedAnnualRate = 0.04m } };
        var first = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);
        var retained = fixture.State;
        var restarted = fixture.CreateService();

        var retry = await restarted.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);

        retry.IsSuccess.Should().BeTrue();
        retry.Value.Should().Be(first.Value);
        fixture.State.Should().Be(retained);
        fixture.State.Contract.TermsVersions.Should().HaveCount(2);
        fixture.State.Servicing.RevisionHistory.Should().ContainSingle();
        fixture.History.Should().ContainSingle();
        fixture.Writes.Should().ContainSingle();
        fixture.OutboxMessages.Should().HaveCount(3);
        fixture.SaveCount.Should().Be(1);
        using var payload = JsonDocument.Parse(fixture.History.Single().PayloadJson);
        payload.RootElement.GetProperty("newTermsVersion").Deserialize<LoanTermsVersionDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().Be(fixture.State.Contract.TermsVersions[1]);

        foreach (var changed in new[]
        {
            request with { HaircutAmount = 25m },
            request with { EffectiveDate = request.EffectiveDate.AddDays(1) },
            request with { NewTerms = request.NewTerms! with { FixedAnnualRate = 0.03m } },
            request with { Reason = "Different approval" },
            request with { RestructuringType = RestructuringType.RateReduction }
        })
        {
            var conflict = await restarted.RestructureLoanAsync(fixture.LoanId, changed, fixture.Metadata);
            conflict.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        }
        fixture.State.Should().Be(retained);
        fixture.SaveCount.Should().Be(1);
    }

    [Theory]
    [InlineData(DirectLendingErrorCode.Conflict, "matching")]
    [InlineData(DirectLendingErrorCode.ConcurrencyConflict, "matching")]
    [InlineData(DirectLendingErrorCode.Conflict, "absent")]
    [InlineData(DirectLendingErrorCode.ConcurrencyConflict, "absent")]
    [InlineData(DirectLendingErrorCode.Conflict, "different")]
    [InlineData(DirectLendingErrorCode.ConcurrencyConflict, "different")]
    public async Task RestructureLoanAsync_StorageConflictRequiresMatchingCommittedReceipt(
        DirectLendingErrorCode conflictCode, string receipt)
    {
        var fixture = new RestructuringCommandFixture
        {
            SimulatedConflict = conflictCode,
            RetainCompetingCommit = receipt != "absent",
            CompetingCommandId = receipt == "different" ? Guid.NewGuid() : null
        };
        var request = fixture.Request(100m);
        if (receipt == "matching")
        {
            var result = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(fixture.State.Contract);
            result.Value!.Status.Should().Be(LoanStatus.Workout, "retry must load the competing commit, not return its initial state");
            fixture.State.Servicing.Balances.PrincipalOutstanding.Should().Be(0m);
            fixture.History.Should().ContainSingle();
            fixture.Writes.Should().ContainSingle();
        }
        else
        {
            var action = () => fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);
            (await action.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(conflictCode);
        }
        fixture.SaveCount.Should().Be(1);
    }

    private sealed class RestructuringCommandFixture
    {
        private readonly IDirectLendingStateStore _store = Substitute.For<IDirectLendingStateStore>();
        private readonly IDirectLendingQueryService _query = Substitute.For<IDirectLendingQueryService>();

        public RestructuringCommandFixture()
        {
            var servicing = BuildServicing(LoanId) with
            {
                Balances = BuildServicing(LoanId).Balances with { PrincipalOutstanding = 100m },
                TotalDrawn = 100m,
                AvailableToDraw = 999_900m,
                DrawdownLots =
                [
                    new(Guid.NewGuid(), new(2026, 3, 22), new(2026, 3, 22), 60m, 60m, "first-wire"),
                    new(Guid.NewGuid(), new(2026, 3, 23), new(2026, 3, 23), 40m, 40m, "second-wire")
                ]
            };
            State = new(LoanId, 7, BuildContract(LoanId), servicing);
            _query.LoadAggregateAsync(LoanId, Arg.Any<CancellationToken>()).Returns(_ => State);
            _query.GetHistoryAsync(LoanId, Arg.Any<CancellationToken>()).Returns(_ => History.ToArray());
            _store.SaveAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<LoanContractDetailDto>(), Arg.Any<LoanServicingStateDto>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<JsonDocument>(),
                Arg.Any<DirectLendingEventWriteMetadata>(), Arg.Any<DirectLendingPersistenceBatch?>(),
                Arg.Any<IReadOnlyList<LedgerJournalEntryWrite>?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    SaveCount++;
                    if (SimulatedConflict is { } uncommittedConflict && !RetainCompetingCommit)
                        throw new DirectLendingCommandException(new(uncommittedConflict, "Injected storage conflict without a receipt."));
                    State = new(LoanId, call.ArgAt<long>(2), call.ArgAt<LoanContractDetailDto>(3), call.ArgAt<LoanServicingStateDto>(4));
                    var metadata = call.ArgAt<DirectLendingEventWriteMetadata>(9);
                    History.Add(new(call.ArgAt<Guid>(12), State.AggregateVersion, call.ArgAt<string>(5), call.ArgAt<int>(6),
                        call.ArgAt<DateOnly?>(7), DateTimeOffset.UtcNow, call.ArgAt<JsonDocument>(8).RootElement.GetRawText(),
                        metadata.CausationId, metadata.CorrelationId, CompetingCommandId ?? metadata.CommandId, metadata.SourceSystem, metadata.ReplayFlag));
                    Writes.AddRange(call.ArgAt<IReadOnlyList<LedgerJournalEntryWrite>?>(11) ?? []);
                    OutboxMessages.AddRange(call.ArgAt<DirectLendingPersistenceBatch?>(10)?.OutboxMessages ?? []);
                    if (SimulatedConflict is { } committedConflict)
                        throw new DirectLendingCommandException(new(committedConflict, "Injected competing commit after the initial history read."));
                    return Task.CompletedTask;
                });
            Service = CreateService();
        }

        public Guid LoanId { get; } = Guid.NewGuid();
        public DirectLendingCommandMetadataDto Metadata { get; } = new(Guid.NewGuid(), null, null, "restructuring-test", false);
        public PersistedDirectLendingState State { get; private set; }
        public List<LoanEventLineageDto> History { get; } = [];
        public List<LedgerJournalEntryWrite> Writes { get; } = [];
        public List<DirectLendingOutboxMessageWrite> OutboxMessages { get; } = [];
        public int SaveCount { get; private set; }
        public DirectLendingErrorCode? SimulatedConflict { get; init; }
        public bool RetainCompetingCommit { get; init; }
        public Guid? CompetingCommandId { get; init; }
        public PostgresDirectLendingCommandService Service { get; }

        public RestructureLoanRequest Request(decimal? haircut) => new(RestructuringType.PrincipalHaircut,
            "Approved principal concession", new(2026, 3, 24), HaircutAmount: haircut);

        public PostgresDirectLendingCommandService CreateService() => new(_store,
            Substitute.For<IDirectLendingOperationsStore>(), _query,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
            new DirectLendingOptions { CurrentEventSchemaVersion = 1 });
    }
}
