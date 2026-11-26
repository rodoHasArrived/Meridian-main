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
    [InlineData("fee")]
    [InlineData("write-off")]
    [InlineData("collateral-add")]
    [InlineData("collateral-remove")]
    [InlineData("collateral-value")]
    [InlineData("status")]
    [InlineData("pik")]
    [InlineData("amortization")]
    public Task LifecycleCommand_SequentialRetryPreservesEvidenceAndRejectsChangedRequest(string kind)
        => AssertLifecycleRetryAsync(kind, null);

    [Theory]
    [InlineData("fee", DirectLendingErrorCode.Conflict)]
    [InlineData("write-off", DirectLendingErrorCode.ConcurrencyConflict)]
    [InlineData("collateral-add", DirectLendingErrorCode.Conflict)]
    [InlineData("collateral-remove", DirectLendingErrorCode.ConcurrencyConflict)]
    [InlineData("collateral-value", DirectLendingErrorCode.Conflict)]
    [InlineData("status", DirectLendingErrorCode.ConcurrencyConflict)]
    [InlineData("pik", DirectLendingErrorCode.Conflict)]
    [InlineData("amortization", DirectLendingErrorCode.ConcurrencyConflict)]
    public Task LifecycleCommand_ConcurrentCommitResolvesOnlyTheSameRetainedRequest(string kind, DirectLendingErrorCode error)
        => AssertLifecycleRetryAsync(kind, error);

    [Fact]
    public async Task LifecycleCommand_ReusedIdConflictsBeforeRevalidatingAChangedRequest()
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-retry", false);
        (await fixture.Service.AssessFeeAsync(fixture.LoanId, new AssessFeeRequest("Admin", 5m, fixture.Date, null), metadata))
            .IsSuccess.Should().BeTrue();

        var changed = await fixture.Service.AssessFeeAsync(fixture.LoanId,
            new AssessFeeRequest("Admin", -1m, fixture.Date, null), metadata);
        var otherKind = await fixture.Service.TogglePikAsync(fixture.LoanId,
            new TogglePikRequest(true, fixture.Date), metadata);

        changed.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        otherKind.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        fixture.SaveAttempts.Should().Be(1);
    }

    private static async Task AssertLifecycleRetryAsync(string kind, DirectLendingErrorCode? conflict)
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-retry", false);
        var invoke = await LifecycleInvocationAsync(fixture, kind, metadata);
        var before = fixture.Current;
        var accepted = await invoke(false);
        accepted.IsSuccess.Should().BeTrue();
        var retainedBytes = JsonSerializer.Serialize(fixture.History);
        var committedState = JsonSerializer.Serialize(fixture.Current);
        var journalCount = fixture.CommittedJournalWrites.Count;
        var saves = fixture.SaveAttempts;

        fixture.SimulateConcurrentCommit(before, conflict);
        var replay = await invoke(false);

        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeSameAs(fixture.Current.Servicing);
        fixture.SaveAttempts.Should().Be(saves + (conflict.HasValue ? 1 : 0));
        fixture.SimulateConcurrentCommit(before, conflict);
        var changed = await invoke(true);

        changed.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        fixture.CommittedJournalWrites.Should().HaveCount(journalCount);
        JsonSerializer.Serialize(fixture.History).Should().Be(retainedBytes);
        JsonSerializer.Serialize(fixture.Current).Should().Be(committedState);
    }

    private static async Task<Func<bool, Task<DirectLendingCommandResult<LoanServicingStateDto>>>> LifecycleInvocationAsync(
        LifecycleRetryFixture fixture, string kind, DirectLendingCommandMetadataDto metadata)
    {
        var collateralId = Guid.Empty;
        if (kind is "collateral-remove" or "collateral-value")
        {
            var added = await fixture.Service.AddCollateralAsync(fixture.LoanId,
                new AddCollateralRequest(CollateralType.Equipment, "Synthetic machine", 200m, CurrencyCode.USD, fixture.Date));
            added.IsSuccess.Should().BeTrue();
            collateralId = fixture.Current.Servicing.Collateral!.Single().CollateralId;
        }
        if (kind == "amortization")
            fixture.Current = fixture.Current with
            {
                Servicing = fixture.Current.Servicing with { UnamortizedDiscount = 100m, UnamortizedPremium = 20m }
            };

        return kind switch
        {
            "fee" => changed => fixture.Service.AssessFeeAsync(fixture.LoanId,
                new AssessFeeRequest("Admin", changed ? 6m : 5m, fixture.Date, null), metadata),
            "write-off" => changed => fixture.Service.ApplyWriteOffAsync(fixture.LoanId,
                new ApplyWriteOffRequest(changed ? 26m : 25m, fixture.Date, "Synthetic loss"), metadata),
            "collateral-add" => changed => fixture.Service.AddCollateralAsync(fixture.LoanId,
                new AddCollateralRequest(CollateralType.Equipment, changed ? "Changed machine" : "Synthetic machine", 200m,
                    CurrencyCode.USD, fixture.Date), metadata),
            "collateral-remove" => changed => fixture.Service.RemoveCollateralAsync(fixture.LoanId,
                new RemoveCollateralRequest(collateralId, changed ? "Changed release" : "Synthetic release"), metadata),
            "collateral-value" => changed => fixture.Service.UpdateCollateralValueAsync(fixture.LoanId,
                new UpdateCollateralValueRequest(collateralId, changed ? 151m : 150m, fixture.Date), metadata),
            "status" => changed => fixture.Service.TransitionLoanStatusAsync(fixture.LoanId,
                new TransitionLoanStatusRequest(changed ? LoanStatus.NonPerforming : LoanStatus.Defaulted, "Synthetic status", fixture.Date), metadata),
            "pik" => changed => fixture.Service.TogglePikAsync(fixture.LoanId,
                new TogglePikRequest(!changed, fixture.Date), metadata),
            "amortization" => changed => fixture.Service.AmortizeDiscountPremiumAsync(fixture.LoanId,
                new AmortizeDiscountPremiumRequest(changed ? fixture.Date.AddDays(1) : fixture.Date), metadata),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private sealed class LifecycleRetryFixture
    {
        public Guid LoanId { get; } = Guid.Parse("81111111-3333-4333-8333-333333333333");
        public DateOnly Date { get; } = new(2026, 3, 24);
        public PersistedDirectLendingState Current { get; set; }
        public List<LoanEventLineageDto> History { get; } = [];
        public List<LedgerJournalEntryWrite> CommittedJournalWrites { get; } = [];
        public int SaveAttempts { get; private set; }
        public PostgresDirectLendingCommandService Service { get; }
        private PersistedDirectLendingState? _staleState;
        private bool _hideHistoryOnce;
        private bool _hideHistoryUntilStateLoad;
        private Exception? _nextSaveFailure;

        public LifecycleRetryFixture()
        {
            var template = BuildContract(LoanId);
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            void Seed(string eventType, object payload)
            {
                History.Add(new LoanEventLineageDto(Guid.NewGuid(), History.Count + 1, eventType, 1,
                    Date, new DateTimeOffset(2026, 3, 24, 12, History.Count, 0, TimeSpan.Zero),
                    JsonSerializer.Serialize(payload, json), null, null, null, "synthetic-retry", false));
            }
            Seed("loan.created", new { loanId = LoanId, template.FacilityName, template.Borrower, template.EffectiveDate, terms = template.CurrentTerms });
            Seed("loan.activated", new { activationDate = Date });
            Seed("loan.drawdown-booked", new { lotId = Guid.NewGuid(), amount = 100m, tradeDate = Date, settleDate = Date });
            Current = new DirectLendingEventRebuilder().Rebuild(LoanId, History)!;

            var query = Substitute.For<IDirectLendingQueryService>();
            query.LoadAggregateAsync(LoanId, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                _hideHistoryUntilStateLoad = false;
                var result = _staleState ?? Current;
                _staleState = null;
                return result;
            });
            query.GetHistoryAsync(LoanId, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (_hideHistoryUntilStateLoad)
                    return Array.Empty<LoanEventLineageDto>();
                if (!_hideHistoryOnce)
                    return History.ToArray();
                _hideHistoryOnce = false;
                return Array.Empty<LoanEventLineageDto>();
            });
            var store = Substitute.For<IDirectLendingStateStore>();
            store.SaveAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
                    Arg.Any<LoanContractDetailDto>(), Arg.Any<LoanServicingStateDto>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<DateOnly?>(), Arg.Any<JsonDocument>(), Arg.Any<DirectLendingEventWriteMetadata>(),
                    Arg.Any<DirectLendingPersistenceBatch?>(), Arg.Any<IReadOnlyList<LedgerJournalEntryWrite>?>(),
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    SaveAttempts++;
                    if (_nextSaveFailure is { } failure)
                    {
                        _nextSaveFailure = null;
                        throw failure;
                    }
                    call.ArgAt<long>(1).Should().Be(Current.AggregateVersion);
                    var metadata = call.ArgAt<DirectLendingEventWriteMetadata>(9);
                    History.Add(new LoanEventLineageDto(call.ArgAt<Guid>(12), call.ArgAt<long>(2), call.ArgAt<string>(5),
                        call.ArgAt<int>(6), call.ArgAt<DateOnly?>(7), DateTimeOffset.UtcNow.AddMinutes(5),
                        call.ArgAt<JsonDocument>(8).RootElement.GetRawText(), metadata.CausationId, metadata.CorrelationId,
                        metadata.CommandId, metadata.SourceSystem, metadata.ReplayFlag));
                    Current = new PersistedDirectLendingState(LoanId, call.ArgAt<long>(2),
                        call.ArgAt<LoanContractDetailDto>(3), call.ArgAt<LoanServicingStateDto>(4));
                    CommittedJournalWrites.AddRange(call.ArgAt<IReadOnlyList<LedgerJournalEntryWrite>?>(11) ?? []);
                    return Task.CompletedTask;
                });
            Service = new PostgresDirectLendingCommandService(store, Substitute.For<IDirectLendingOperationsStore>(), query,
                new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
                new DirectLendingOptions());
        }

        public void SimulateConcurrentCommit(PersistedDirectLendingState before, DirectLendingErrorCode? error)
        {
            if (!error.HasValue)
                return;
            SimulateConcurrentCommit(before,
                new DirectLendingCommandException(new DirectLendingCommandError(error.Value, "Synthetic concurrent commit")));
        }

        public void SimulateConcurrentCommit(PersistedDirectLendingState before, Exception error)
        {
            _staleState = before;
            _hideHistoryOnce = true;
            _nextSaveFailure = error;
        }

        public void FailNextSave(Exception error) => _nextSaveFailure = error;

        public void RevealCommittedHistoryWhenStateLoads() => _hideHistoryUntilStateLoad = true;
    }
}
