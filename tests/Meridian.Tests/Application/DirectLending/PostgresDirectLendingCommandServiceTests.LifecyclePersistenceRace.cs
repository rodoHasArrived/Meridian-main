using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Tests.Application.DirectLending;

public sealed partial class PostgresDirectLendingCommandServiceTests
{
    [Theory]
    [InlineData("40001", null, "matching")]
    [InlineData("40001", null, "absent")]
    [InlineData("40001", null, "different")]
    [InlineData("23505", "ux_loan_event_command_id", "matching")]
    [InlineData("23505", "ux_loan_event_command_id", "absent")]
    [InlineData("23505", "ux_loan_event_command_id", "different")]
    public async Task RestructureLoanAsync_PostgresRaceRequiresTheMatchingCommittedReceipt(string sqlState, string? constraint, string receipt)
    {
        var failure = LifecyclePostgresFailure(sqlState, constraint);
        var fixture = new RestructuringCommandFixture
        {
            SimulatedStorageFailure = failure,
            RetainCompetingCommit = receipt != "absent",
            CompetingCommandId = receipt == "different" ? Guid.NewGuid() : null
        };
        var request = fixture.Request(100m);
        if (receipt == "matching")
        {
            var result = await fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(fixture.State.Contract);
            fixture.State.Servicing.Balances.PrincipalOutstanding.Should().Be(0m);
            fixture.History.Should().ContainSingle();
            fixture.Writes.Should().ContainSingle();
            fixture.OutboxMessages.Should().HaveCount(3);
        }
        else
        {
            var action = () => fixture.Service.RestructureLoanAsync(fixture.LoanId, request, fixture.Metadata);
            (await action.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(failure);
        }
        fixture.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task RestructureLoanAsync_UnrelatedUniqueViolation_PropagatesDespiteMatchingReceipt()
    {
        var failure = LifecyclePostgresFailure("23505", "unrelated_unique_constraint");
        var fixture = new RestructuringCommandFixture
        {
            SimulatedStorageFailure = failure,
            RetainCompetingCommit = true
        };
        var action = () => fixture.Service.RestructureLoanAsync(fixture.LoanId, fixture.Request(25m), fixture.Metadata);

        (await action.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(failure);
        fixture.SaveCount.Should().Be(1);
        fixture.History.Should().ContainSingle();
        fixture.Writes.Should().ContainSingle();
    }

    [Theory]
    [InlineData("40001", null)]
    [InlineData("23505", "ux_loan_event_command_id")]
    public async Task LifecycleCommand_PostgresRaceResolvesOnlyTheExactRetainedRequest(string sqlState, string? constraint)
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-race", false);
        var request = new AssessFeeRequest("Admin", 5m, fixture.Date, null);
        var before = fixture.Current;
        (await fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata)).IsSuccess.Should().BeTrue();
        var retained = JsonSerializer.Serialize(fixture.History);
        var state = JsonSerializer.Serialize(fixture.Current);
        var writes = fixture.CommittedJournalWrites.Count;

        fixture.SimulateConcurrentCommit(before, LifecyclePostgresFailure(sqlState, constraint));
        var replay = await fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata);
        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeSameAs(fixture.Current.Servicing);

        fixture.SimulateConcurrentCommit(before, LifecyclePostgresFailure(sqlState, constraint));
        var changed = await fixture.Service.AssessFeeAsync(fixture.LoanId, request with { Amount = 6m }, metadata);
        changed.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        fixture.CommittedJournalWrites.Should().HaveCount(writes);
        JsonSerializer.Serialize(fixture.History).Should().Be(retained);
        JsonSerializer.Serialize(fixture.Current).Should().Be(state);
        fixture.SaveAttempts.Should().Be(3, "a recognized race rechecks receipts without retrying a transaction");
    }

    [Theory]
    [InlineData("40001", null)]
    [InlineData("23505", "ux_loan_event_command_id")]
    public async Task LifecycleCommand_PostgresRaceWithoutCommittedReceipt_PropagatesOriginalFailure(string sqlState, string? constraint)
    {
        var fixture = new LifecycleRetryFixture();
        var failure = LifecyclePostgresFailure(sqlState, constraint);
        var retained = JsonSerializer.Serialize(fixture.History);
        fixture.FailNextSave(failure);

        var action = () => fixture.Service.AssessFeeAsync(fixture.LoanId,
            new AssessFeeRequest("Admin", 5m, fixture.Date, null),
            new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-race", false));

        (await action.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(failure);
        fixture.SaveAttempts.Should().Be(1);
        fixture.CommittedJournalWrites.Should().BeEmpty();
        JsonSerializer.Serialize(fixture.History).Should().Be(retained);
    }

    [Theory]
    [InlineData("23505", "unrelated_unique_constraint")]
    [InlineData("23505", null)]
    [InlineData("23503", null)]
    [InlineData("XX000", null)]
    public async Task LifecycleCommand_UnrelatedPostgresFailure_PropagatesEvenWhenMatchingReceiptExists(string sqlState, string? constraint)
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-race", false);
        var request = new AssessFeeRequest("Admin", 5m, fixture.Date, null);
        var before = fixture.Current;
        await fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata);
        var failure = LifecyclePostgresFailure(sqlState, constraint);
        fixture.SimulateConcurrentCommit(before, failure);

        var action = () => fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata);

        (await action.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(failure);
        fixture.SaveAttempts.Should().Be(2);
        fixture.History.Should().HaveCount(4);
        fixture.CommittedJournalWrites.Should().ContainSingle();
    }

    [Fact]
    public async Task LifecycleCommand_UnrelatedStorageFailure_IsNotConvertedToSuccessfulRetry()
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-race", false);
        var request = new AssessFeeRequest("Admin", 5m, fixture.Date, null);
        var before = fixture.Current;
        await fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata);
        var failure = new IOException("Synthetic unrelated storage failure");
        fixture.SimulateConcurrentCommit(before, failure);

        var action = () => fixture.Service.AssessFeeAsync(fixture.LoanId, request, metadata);

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        fixture.CommittedJournalWrites.Should().ContainSingle();
    }

    [Theory]
    [InlineData("collateral-remove")]
    [InlineData("collateral-value")]
    [InlineData("amortization")]
    public async Task LifecycleCommand_NewerStateCannotBypassChangedRequestCheckThroughAnEarlyNoOp(string kind)
    {
        var fixture = new LifecycleRetryFixture();
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-race", false);
        var invoke = await LifecycleInvocationAsync(fixture, kind, metadata);
        (await invoke(false)).IsSuccess.Should().BeTrue();
        if (kind == "collateral-value")
        {
            var collateral = fixture.Current.Servicing.Collateral!.Single();
            await fixture.Service.RemoveCollateralAsync(fixture.LoanId, new RemoveCollateralRequest(collateral.CollateralId, "Later release"));
        }
        if (kind == "amortization")
            (await fixture.Service.AmendTermsAsync(fixture.LoanId, new AmendLoanTermsRequest(
                fixture.Current.Contract.CurrentTerms with { MaturityDate = fixture.Date.AddDays(-1) }, "Later maturity change")))
                .IsSuccess.Should().BeTrue();
        var retained = JsonSerializer.Serialize(fixture.History);
        var state = JsonSerializer.Serialize(fixture.Current);
        var writes = fixture.CommittedJournalWrites.Count;
        var saves = fixture.SaveAttempts;

        // A history read before loading this state would see no winning receipt. Loading the
        // newer state makes that receipt visible; every early no-op must check it afterwards.
        fixture.RevealCommittedHistoryWhenStateLoads();
        var replay = await invoke(false);
        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeSameAs(fixture.Current.Servicing);
        fixture.RevealCommittedHistoryWhenStateLoads();
        var changed = await invoke(true);

        changed.Error!.Code.Should().Be(DirectLendingErrorCode.Conflict);
        fixture.SaveAttempts.Should().Be(saves);
        fixture.CommittedJournalWrites.Should().HaveCount(writes);
        JsonSerializer.Serialize(fixture.History).Should().Be(retained);
        JsonSerializer.Serialize(fixture.Current).Should().Be(state);
    }

    private static PostgresException LifecyclePostgresFailure(string sqlState, string? constraint)
        => new("Synthetic storage failure", "ERROR", "ERROR", sqlState, constraintName: constraint);
}
