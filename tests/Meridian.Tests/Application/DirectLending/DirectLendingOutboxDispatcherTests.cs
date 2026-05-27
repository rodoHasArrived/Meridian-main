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

        var processAsync = typeof(DirectLendingOutboxDispatcher).GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        processAsync.Should().NotBeNull();
        var task = (Task)processAsync!.Invoke(dispatcher, [message, CancellationToken.None])!;
        await task;

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
