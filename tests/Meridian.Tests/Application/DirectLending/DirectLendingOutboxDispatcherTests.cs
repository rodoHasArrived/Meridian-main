using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingOutboxDispatcherTests
{
    [Fact]
    public async Task ProcessJournalAsync_StampsSharedAccountingPolicyLineageOnDirectLendingJournalLines()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        JournalEntryDto? savedEntry = null;
        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var sourceEvent = new LoanEventLineageDto(
            sourceEventId,
            AggregateVersion: 2,
            EventType: "loan.daily-accrual-posted",
            EventSchemaVersion: 1,
            EffectiveDate: new DateOnly(2026, 5, 13),
            RecordedAt: DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            PayloadJson: """{"interestAmount":125.25,"commitmentFeeAmount":0}""",
            CausationId: null,
            CorrelationId: null,
            CommandId: null,
            SourceSystem: "test",
            ReplayFlag: false);

        queryService.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([sourceEvent]);
        queryService.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        queryService.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(BuildLoanContract(loanId));
        operationsStore
            .SaveJournalEntryAsync(Arg.Do<JournalEntryDto>(entry => savedEntry = entry), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<JournalEntryDto>());

        var payload = JsonSerializer.Serialize(new
        {
            loanId,
            sourceEventId,
            eventType = sourceEvent.EventType,
            effectiveDate = "2026-05-13",
            servicingRevision = 2,
            commandId = (Guid?)null,
            correlationId = (Guid?)null,
            causationId = (Guid?)null,
            sourceSystem = "test"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var message = new DirectLendingOutboxMessage(
            Guid.NewGuid(),
            "direct-lending.journal.requested",
            loanId.ToString("N"),
            payload,
            HeadersJson: null,
            OccurredAt: DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            VisibleAfter: DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            ProcessedAt: null,
            ErrorCount: 0,
            LastError: null);

        await InvokeProcessAsync(dispatcher, message);

        savedEntry.Should().NotBeNull();
        savedEntry!.LedgerBasis.Should().Be("Primary");
        savedEntry.SourceEventId.Should().Be(sourceEventId);
        savedEntry.Lines.Should().HaveCount(2);
        savedEntry.Lines.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.DimensionsJson));
        using var dimensions = JsonDocument.Parse(savedEntry.Lines[0].DimensionsJson!);
        dimensions.RootElement.GetProperty("accountingBasis").GetString().Should().Be("Primary");
        dimensions.RootElement.GetProperty("accountingPolicyId").GetString().Should().Be("legacy-v1");
        dimensions.RootElement.GetProperty("accountingPolicyVersion").GetString().Should().Be("legacy-v1");
        dimensions.RootElement.GetProperty("ruleId").GetString().Should().Be(sourceEvent.EventType);
        dimensions.RootElement.GetProperty("sourceEventId").GetGuid().Should().Be(sourceEventId);
    }

    [Fact]
    public async Task ProcessJournalAsync_UsesCamelCasePrepaymentPenaltyPayloadFromPersistedEvents()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        JournalEntryDto? savedEntry = null;
        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var sourceEvent = new LoanEventLineageDto(
            sourceEventId,
            AggregateVersion: 3,
            EventType: "loan.prepayment-penalty-charged",
            EventSchemaVersion: 1,
            EffectiveDate: new DateOnly(2026, 5, 14),
            RecordedAt: DateTimeOffset.Parse("2026-05-14T12:00:00Z"),
            PayloadJson: """{"outstandingPrincipal":500000,"penaltyAmount":10000,"effectiveDate":"2026-05-14"}""",
            CausationId: null,
            CorrelationId: null,
            CommandId: null,
            SourceSystem: "test",
            ReplayFlag: false);

        queryService.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([sourceEvent]);
        queryService.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        queryService.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(BuildLoanContract(loanId));
        operationsStore
            .SaveJournalEntryAsync(Arg.Do<JournalEntryDto>(entry => savedEntry = entry), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<JournalEntryDto>());

        await InvokeProcessAsync(dispatcher, BuildJournalMessage(loanId, sourceEventId, sourceEvent.EventType, sourceEvent.EffectiveDate));

        savedEntry.Should().NotBeNull();
        savedEntry!.Description.Should().Be("Prepayment penalty");
        savedEntry.Lines.Should().ContainSingle(line => line.AccountCode == "PenaltyReceivable" && line.DebitAmount == 10_000m);
        savedEntry.Lines.Should().ContainSingle(line => line.AccountCode == "PenaltyIncome" && line.CreditAmount == 10_000m);
    }

    [Fact]
    public async Task ProcessJournalAsync_SkipsPrepaymentPenaltyReplayWhenSourceEventAlreadyHasJournal()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var sourceEvent = new LoanEventLineageDto(
            sourceEventId,
            AggregateVersion: 3,
            EventType: "loan.prepayment-penalty-charged",
            EventSchemaVersion: 1,
            EffectiveDate: new DateOnly(2026, 5, 14),
            RecordedAt: DateTimeOffset.Parse("2026-05-14T12:00:00Z"),
            PayloadJson: """{"outstandingPrincipal":500000,"penaltyAmount":10000,"effectiveDate":"2026-05-14"}""",
            CausationId: null,
            CorrelationId: null,
            CommandId: null,
            SourceSystem: "test",
            ReplayFlag: false);
        var existingJournal = new JournalEntryDto(
            Guid.NewGuid(),
            loanId,
            new DateOnly(2026, 5, 14),
            new DateOnly(2026, 5, 14),
            sourceEventId,
            "loan.prepayment-penalty-charged",
            "Primary",
            "Prepayment penalty",
            DateTimeOffset.Parse("2026-05-14T12:01:00Z"),
            PostedAt: null,
            JournalEntryStatus.Draft,
            Lines: []);

        queryService.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([sourceEvent]);
        queryService.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns([existingJournal]);
        queryService.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(BuildLoanContract(loanId));

        await InvokeProcessAsync(dispatcher, BuildJournalMessage(loanId, sourceEventId, sourceEvent.EventType, sourceEvent.EffectiveDate));

        await operationsStore.DidNotReceiveWithAnyArgs().SaveJournalEntryAsync(default!, default);
        await operationsStore.Received(1).MarkOutboxProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxFailedAsync(default, default!, default);
    }

    [Fact]
    public async Task ProcessJournalAsync_UsesAppliedAmountForCamelCaseWriteOffPayload()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        JournalEntryDto? savedEntry = null;
        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var sourceEvent = new LoanEventLineageDto(
            sourceEventId,
            AggregateVersion: 4,
            EventType: "loan.write-off-applied",
            EventSchemaVersion: 1,
            EffectiveDate: new DateOnly(2026, 5, 15),
            RecordedAt: DateTimeOffset.Parse("2026-05-15T12:00:00Z"),
            PayloadJson: """{"requestedAmount":7500,"appliedAmount":7500,"effectiveDate":"2026-05-15","reason":"charge-off"}""",
            CausationId: null,
            CorrelationId: null,
            CommandId: null,
            SourceSystem: "test",
            ReplayFlag: false);

        queryService.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([sourceEvent]);
        queryService.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        queryService.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(BuildLoanContract(loanId));
        operationsStore
            .SaveJournalEntryAsync(Arg.Do<JournalEntryDto>(entry => savedEntry = entry), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<JournalEntryDto>());

        await InvokeProcessAsync(dispatcher, BuildJournalMessage(loanId, sourceEventId, sourceEvent.EventType, sourceEvent.EffectiveDate));

        savedEntry.Should().NotBeNull();
        savedEntry!.Description.Should().Be("Write-off");
        savedEntry.Lines.Should().ContainSingle(line => line.AccountCode == "WriteOffExpense" && line.DebitAmount == 7_500m);
        savedEntry.Lines.Should().ContainSingle(line => line.AccountCode == "LoanPrincipal" && line.CreditAmount == 7_500m);
    }

    [Fact]
    public async Task ProcessJournalAsync_IncludesPenaltyLinesForDailyAccrualPayload()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        var loanId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        JournalEntryDto? savedEntry = null;
        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions(),
            NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var sourceEvent = new LoanEventLineageDto(
            sourceEventId,
            AggregateVersion: 5,
            EventType: "loan.daily-accrual-posted",
            EventSchemaVersion: 1,
            EffectiveDate: new DateOnly(2026, 5, 16),
            RecordedAt: DateTimeOffset.Parse("2026-05-16T12:00:00Z"),
            PayloadJson: """{"interestAmount":125.25,"commitmentFeeAmount":0,"penaltyAmount":25.5}""",
            CausationId: null,
            CorrelationId: null,
            CommandId: null,
            SourceSystem: "test",
            ReplayFlag: false);

        queryService.GetHistoryAsync(loanId, Arg.Any<CancellationToken>()).Returns([sourceEvent]);
        queryService.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        queryService.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(BuildLoanContract(loanId));
        operationsStore
            .SaveJournalEntryAsync(Arg.Do<JournalEntryDto>(entry => savedEntry = entry), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<JournalEntryDto>());

        await InvokeProcessAsync(dispatcher, BuildJournalMessage(loanId, sourceEventId, sourceEvent.EventType, sourceEvent.EffectiveDate));

        savedEntry.Should().NotBeNull();
        savedEntry!.Lines.Should().ContainSingle(line => line.AccountCode == "PenaltyReceivable" && line.DebitAmount == 25.5m);
        savedEntry.Lines.Should().ContainSingle(line => line.AccountCode == "PenaltyIncome" && line.CreditAmount == 25.5m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPollingStoreThrows_KeepsFailureInsideWorkerLoop()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        using var cts = new CancellationTokenSource();
        var pollAttempts = 0;

        operationsStore
            .GetPendingOutboxMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pollAttempts++;
                cts.Cancel();
                return Task.FromException<IReadOnlyList<DirectLendingOutboxMessage>>(new InvalidOperationException("Database unavailable."));
            });

        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions
            {
                OutboxBatchSize = 25,
                OutboxPollIntervalSeconds = 1
            },
            NullLogger<DirectLendingOutboxDispatcher>.Instance);

        var act = async () =>
        {
            var executeAsync = typeof(DirectLendingOutboxDispatcher).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            executeAsync.Should().NotBeNull();

            var task = (Task)executeAsync!.Invoke(dispatcher, new object[] { cts.Token })!;
            await task.ConfigureAwait(false);
        };

        await act.Should().NotThrowAsync();
        pollAttempts.Should().Be(1);
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxProcessedAsync(default, default);
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxFailedAsync(default, default!, default);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(10_000, 5000)]
    public void NormalizeOutboxBatchSize_BoundsEnvironmentDrivenWorkerBatch(int configuredBatchSize, int expectedBatchSize)
    {
        DirectLendingOutboxDispatcher.NormalizeOutboxBatchSize(configuredBatchSize)
            .Should().Be(expectedBatchSize);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(15, 15)]
    [InlineData(10_000, 3600)]
    public void NormalizeOutboxPollInterval_BoundsEnvironmentDrivenWorkerDelay(int configuredPollIntervalSeconds, int expectedSeconds)
    {
        DirectLendingOutboxDispatcher.NormalizeOutboxPollInterval(configuredPollIntervalSeconds)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchConfigIsInvalid_ClaimsAtLeastOnePendingMessage()
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        using var cts = new CancellationTokenSource();

        operationsStore
            .GetPendingOutboxMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromException<IReadOnlyList<DirectLendingOutboxMessage>>(
                    new InvalidOperationException("Stop after first normalized poll."));
            });

        var dispatcher = new DirectLendingOutboxDispatcher(
            operationsStore,
            commandService,
            queryService,
            new DirectLendingOptions
            {
                OutboxBatchSize = 0,
                OutboxPollIntervalSeconds = 0
            },
            NullLogger<DirectLendingOutboxDispatcher>.Instance);

        var executeAsync = typeof(DirectLendingOutboxDispatcher)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        executeAsync.Should().NotBeNull();

        var task = (Task)executeAsync!.Invoke(dispatcher, [cts.Token])!;
        await task.ConfigureAwait(false);

        await operationsStore.Received(1).GetPendingOutboxMessagesAsync(1, Arg.Any<CancellationToken>());
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxProcessedAsync(default, default);
        await operationsStore.DidNotReceiveWithAnyArgs().MarkOutboxFailedAsync(default, default!, default);
    }

    private static async Task InvokeProcessAsync(DirectLendingOutboxDispatcher dispatcher, DirectLendingOutboxMessage message)
    {
        var processAsync = typeof(DirectLendingOutboxDispatcher).GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        processAsync.Should().NotBeNull();
        var task = (Task)processAsync!.Invoke(dispatcher, [message, CancellationToken.None])!;
        await task;
    }

    private static DirectLendingOutboxMessage BuildJournalMessage(
        Guid loanId,
        Guid sourceEventId,
        string eventType,
        DateOnly? effectiveDate)
    {
        var payload = JsonSerializer.Serialize(new
        {
            loanId,
            sourceEventId,
            eventType,
            effectiveDate,
            servicingRevision = 2,
            commandId = (Guid?)null,
            correlationId = (Guid?)null,
            causationId = (Guid?)null,
            sourceSystem = "test"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new DirectLendingOutboxMessage(
            Guid.NewGuid(),
            "direct-lending.journal.requested",
            loanId.ToString("N"),
            payload,
            HeadersJson: null,
            OccurredAt: DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            VisibleAfter: DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            ProcessedAt: null,
            ErrorCount: 0,
            LastError: null);
    }

    private static LoanContractDetailDto BuildLoanContract(Guid loanId)
    {
        var effectiveDate = new DateOnly(2026, 5, 1);
        var terms = new DirectLendingTermsDto(
            OriginationDate: effectiveDate,
            MaturityDate: new DateOnly(2028, 5, 1),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.085m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Monthly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0m,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);

        return new LoanContractDetailDto(
            loanId,
            FacilityName: "Warehouse loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Acme Borrower LLC", LegalEntityId: null),
            Status: LoanStatus.Active,
            EffectiveDate: effectiveDate,
            ActivationDate: effectiveDate,
            CloseDate: null,
            CurrentTermsVersion: 1,
            CurrentTerms: terms,
            TermsVersions:
            [
                new LoanTermsVersionDto(
                    VersionNumber: 1,
                    TermsHash: "terms-v1",
                    Terms: terms,
                    SourceAction: "create",
                    AmendmentReason: null,
                    RecordedAt: DateTimeOffset.Parse("2026-05-01T12:00:00Z"))
            ]);
    }
}
