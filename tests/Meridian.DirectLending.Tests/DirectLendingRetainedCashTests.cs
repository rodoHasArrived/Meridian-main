using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.DirectLending.Tests;

[Trait("Category", "Integration")]
public sealed class DirectLendingRetainedCashTests
{
    private static readonly DateOnly Origination = new(2026, 3, 22);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [DirectLendingDatabaseFact]
    public async Task UnappliedReceipt_PersistsEveryCentAndReplaysWithoutRepeatingAccounting()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var payment = new ApplyMixedPaymentRequest(125.123m, Origination.AddDays(1), null, "cash-held");
        var received = await db.Service.ApplyMixedPaymentAsync(loan.LoanId, payment);
        received!.Balances.UnappliedCash.Should().Be(125.123m);
        received.Balances.PrincipalOutstanding.Should().Be(1_000m);
        var projected = await db.Store.LoadServicingProjectionAsync(loan.LoanId);
        projected!.Balances.Should().BeEquivalentTo(received.Balances, options => options.WithStrictOrdering());

        var cash = (await db.Store.GetCashTransactionsAsync(loan.LoanId)).Should().ContainSingle().Subject;
        cash.Amount.Should().Be(payment.Amount);
        var allocation = (await db.Store.GetPaymentAllocationsAsync(loan.LoanId)).Should().ContainSingle().Subject;
        allocation.TargetType.Should().Be("UnappliedCash");
        allocation.AllocatedAmount.Should().Be(payment.Amount);
        allocation.SourceEventId.Should().Be(cash.SourceEventId);
        AssertReceiptJournal((await db.GetLedgerEvidenceAsync(loan.LoanId, "loan.mixed-payment-applied")).Single(),
            "UnappliedCash", payment.Amount);

        var before = await CountArtifactsAsync(db, loan.LoanId);
        await db.DeleteLiveStateAsync(loan.LoanId);
        var rebuilt = await db.QueryService.LoadAggregateAsync(loan.LoanId);
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(received.Balances, options => options.WithStrictOrdering());
        rebuilt.Servicing.DrawdownLots.Should().BeEquivalentTo(received.DrawdownLots);
        rebuilt.Contract.TermsVersions.Single().TermsHash.Should().Be(loan.TermsVersions.Single().TermsHash);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);

        var run = await db.Service.ReconcileAsync(loan.LoanId);
        (await db.Store.GetReconciliationResultsAsync(run!.ReconciliationRunId)).Should().Contain(result =>
            result.CashTransactionId == cash.CashTransactionId && result.MatchStatus == "UnappliedCashHeld" &&
            result.ExpectedAmount == payment.Amount && result.ActualAmount == payment.Amount && result.VarianceAmount == 0m);
    }

    [DirectLendingDatabaseFact]
    public async Task PrepaidInterest_RemainsALiabilityAcrossAccrualAndRetainsExplicitTerms()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var policy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-contract/interest-prepayment-clause");
        var loan = await CreateDrawnLoanAsync(db, policy);
        var payment = new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "prepaid-interest");
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId, payment);
        var projectedContract = await db.Store.LoadContractProjectionAsync(loan.LoanId);
        projectedContract!.CurrentTerms.Should().Be(loan.CurrentTerms);
        projectedContract.CurrentTerms.ExcessPaymentPolicy.Should().Be(policy);
        projectedContract.TermsVersions.Single().TermsHash.Should().Be(loan.TermsVersions.Single().TermsHash);
        JsonSerializer.Serialize(projectedContract.CurrentTerms, JsonOptions).Should()
            .Be(JsonSerializer.Serialize(loan.CurrentTerms, JsonOptions));
        AssertReceiptJournal((await db.GetLedgerEvidenceAsync(loan.LoanId, "loan.mixed-payment-applied")).Single(), "PrepaidInterest", 100m);
        var allocation = (await db.Store.GetPaymentAllocationsAsync(loan.LoanId)).Should().ContainSingle().Subject;
        allocation.TargetType.Should().Be("PrepaidInterest");

        // No contractual release schedule was supplied. Ordinary accrual cannot
        // silently elect one or consume this separately retained liability.
        await db.Service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(Origination.AddDays(2)));
        var state = await db.Store.LoadServicingProjectionAsync(loan.LoanId);
        state!.Balances.PrepaidInterest.Should().Be(100m);
        state.Balances.InterestAccruedUnpaid.Should().Be(0.22m);
        var rebuilt = await db.QueryService.RebuildStateFromHistoryAsync(loan.LoanId);
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(state.Balances, options => options.WithStrictOrdering());
    }

    [DirectLendingDatabaseFact]
    public async Task FutureCashInterest_UsesRetainedReceiptInstructionAndReleasesExactlyOnceAcrossReplay()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var policy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest,
            "synthetic-contract/future-earned-cash-interest", PrepaidInterestApplication.FutureCashInterest);
        var loan = await CreateDrawnLoanAsync(db, policy);
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(0.30m, Origination.AddDays(1), null, "prepaid-future-interest"));
        var cash = (await db.Store.GetCashTransactionsAsync(loan.LoanId)).Single();
        var receiptDateAccrual = await db.Service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(Origination.AddDays(1)));
        receiptDateAccrual!.PrepaidInterestApplied.Should().Be(0m, "this election is for subsequent accrual dates");
        await db.Service.AmendTermsAsync(loan.LoanId, new AmendLoanTermsRequest(loan.CurrentTerms with
        {
            ExcessPaymentPolicy = policy with { Application = PrepaidInterestApplication.HoldForReview }
        }, "The later election applies only to later receipts"));

        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-accrual-retry", false);
        var request = new PostDailyAccrualRequest(Origination.AddDays(2));
        var deliveries = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => db.CommandService.PostDailyAccrualAsync(loan.LoanId, request, metadata)));
        deliveries.Should().OnlyContain(result => result.IsSuccess && result.Value!.PrepaidInterestApplied == 0.22m);
        deliveries.Select(item => item.Value!.AccrualEntryId).Distinct().Should().ContainSingle();
        var state = (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!;
        state.Balances.PrepaidInterest.Should().Be(0.08m);
        state.Balances.InterestAccruedUnpaid.Should().Be(0.22m, "pre-existing earned interest is not retroactively consumed");
        var lot = state.Balances.PrepaidInterestLots!.Should().ContainSingle().Subject;
        lot.CashTransactionId.Should().Be(cash.CashTransactionId);
        lot.Application.Should().Be(PrepaidInterestApplication.FutureCashInterest);
        lot.OriginalAmount.Should().Be(0.30m);
        lot.RemainingAmount.Should().Be(0.08m);
        state.AccrualEntries.Single(entry => entry.AccrualDate == request.AccrualDate).PrepaidInterestReleases
            .Should().BeEquivalentTo(new[] { new PrepaidInterestReleaseDto(cash.CashTransactionId, 0.22m) });

        var beforeRetry = await CountArtifactsAsync(db, loan.LoanId);
        var retry = await db.CommandService.PostDailyAccrualAsync(loan.LoanId, request, metadata);
        retry.Value.Should().BeEquivalentTo(deliveries[0].Value);
        Func<Task> changed = () => db.CommandService.PostDailyAccrualAsync(loan.LoanId,
            new PostDailyAccrualRequest(Origination.AddDays(3)), metadata);
        (await changed.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(beforeRetry);

        var exhausted = await db.Service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(Origination.AddDays(3)));
        exhausted!.PrepaidInterestApplied.Should().Be(0.08m);
        state = (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!;
        state.Balances.PrepaidInterest.Should().Be(0m);
        state.Balances.PrepaidInterestLots!.Single().RemainingAmount.Should().Be(0m, "the retained instruction survives exhaustion");
        state.Balances.InterestAccruedUnpaid.Should().Be(0.36m);
        (await db.Store.GetCashTransactionsAsync(loan.LoanId)).Should().ContainSingle();
        (await db.Store.GetPaymentAllocationsAsync(loan.LoanId)).Should().ContainSingle();
        (await db.Store.GetHistoryAsync(loan.LoanId)).Count(item => item.CommandId == metadata.CommandId).Should().Be(1);

        var journalLines = (await db.GetLedgerEvidenceAsync(loan.LoanId, "loan.daily-accrual-posted"))
            .SelectMany(json => JsonDocument.Parse(json).RootElement.GetProperty("entry").GetProperty("lines")
                .EnumerateArray().Select(line => line.Clone())).ToArray();
        journalLines.Should().NotContain(line => line.GetProperty("account").GetProperty("name").GetString() == "Cash");
        journalLines.Sum(line => line.GetProperty("debit").GetDecimal())
            .Should().Be(journalLines.Sum(line => line.GetProperty("credit").GetDecimal()));
        journalLines.Where(line => line.GetProperty("account").GetProperty("name").GetString() == "PrepaidInterest")
            .Sum(line => line.GetProperty("debit").GetDecimal()).Should().Be(0.30m);
        journalLines.Where(line => line.GetProperty("account").GetProperty("name").GetString() == "InterestIncome")
            .Sum(line => line.GetProperty("credit").GetDecimal()).Should().Be(0.66m, "income follows earned accruals only");

        var beforeReplay = await CountArtifactsAsync(db, loan.LoanId);
        await db.DeleteLiveStateAsync(loan.LoanId);
        var rebuilt = await db.QueryService.LoadAggregateAsync(loan.LoanId);
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(state.Balances, options => options.WithStrictOrdering());
        (await db.Store.LoadAccrualEntryProjectionsAsync(loan.LoanId)).Should()
            .BeEquivalentTo(rebuilt.Servicing.AccrualEntries, options => options.Excluding(entry => entry.RecordedAt).WithStrictOrdering());
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(beforeReplay);
        (await db.CommandService.PostDailyAccrualAsync(loan.LoanId, request, metadata)).Value
            .Should().BeEquivalentTo(deliveries[0].Value);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(beforeReplay);
        var run = await db.Service.ReconcileAsync(loan.LoanId);
        (await db.Store.GetReconciliationResultsAsync(run!.ReconciliationRunId)).Should().Contain(item =>
            item.CashTransactionId == cash.CashTransactionId && item.MatchStatus == "CashAllocationBalanced" &&
            item.Notes.Any(note => note.Contains("remaining prepaid credit: 0", StringComparison.Ordinal)));
    }

    [DirectLendingDatabaseFact]
    public async Task PrepaidReleaseSnapshotFailure_RollsBackTheEarlyEventLiabilityAndJournalTogether()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var policy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest,
            "synthetic-contract/atomic-release", PrepaidInterestApplication.FutureCashInterest);
        var loan = await CreateDrawnLoanAsync(db, policy);
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(1m, Origination.AddDays(1), null, "release-rollback"));
        await db.Service.AmendTermsAsync(loan.LoanId,
            new AmendLoanTermsRequest(loan.CurrentTerms with { ExcessPaymentPolicy = null }, "Prospective amendment"));
        var before = await CountArtifactsAsync(db, loan.LoanId);
        var retained = (await db.Store.LoadAsync(loan.LoanId))!;
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            create function {db.Schema}.reject_release_snapshot() returns trigger language plpgsql as $test$
            begin raise exception 'Injected failure after prepaid-release ledger append'; end $test$;
            create trigger reject_release_snapshot before insert on {db.Schema}.loan_snapshot
                for each row execute function {db.Schema}.reject_release_snapshot();
            """;
        await command.ExecuteNonQueryAsync();
        Func<Task> release = () => db.Service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(Origination.AddDays(2)));
        await release.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        (await db.Store.LoadAsync(loan.LoanId)).Should().BeEquivalentTo(retained);
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances
            .Should().BeEquivalentTo(retained.Servicing.Balances, options => options.WithStrictOrdering());
    }

    [DirectLendingDatabaseFact]
    public async Task FractionalPayment_PreservesBalancedOperationalJournalAndPrincipalProjection()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db,
            new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-fractional-contract"));
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId, new ApplyMixedPaymentRequest(
            0.010m, Origination.AddDays(1), new PaymentBreakdownDto(0m, 0m, 0m, 0m, 0.005m), "fractional-cash"));
        var state = await db.Store.LoadServicingProjectionAsync(loan.LoanId);
        state!.Balances.PrincipalOutstanding.Should().Be(999.995m);
        state.Balances.PrepaidInterest.Should().Be(0.005m);
        state.DrawdownLots.Single().RemainingPrincipal.Should().Be(999.995m);

        var sourceEvent = (await db.Store.GetHistoryAsync(loan.LoanId)).Single(item => item.EventType == "loan.mixed-payment-applied");
        var journalMessage = (await db.Store.GetPendingOutboxMessagesAsync(100)).Single(item =>
            item.Topic == "direct-lending.journal.requested" && item.PayloadJson.Contains(sourceEvent.EventId.ToString("D"), StringComparison.Ordinal));
        using var dispatcher = new DirectLendingOutboxDispatcher(db.Store, db.CommandService, db.QueryService,
            db.Options, NullLogger<DirectLendingOutboxDispatcher>.Instance);
        // Invoke one retained delivery deterministically rather than starting an
        // unrelated projection/publication polling loop in this storage regression.
        var process = typeof(DirectLendingOutboxDispatcher).GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)process.Invoke(dispatcher, [journalMessage, CancellationToken.None])!);
        var operational = (await db.Store.GetJournalEntriesAsync(loan.LoanId)).Single(item => item.SourceEventId == sourceEvent.EventId);
        operational.Lines.Sum(line => line.DebitAmount).Should().Be(0.010m);
        operational.Lines.Sum(line => line.CreditAmount).Should().Be(0.010m);
        operational.Lines.Single(line => line.AccountCode == "LoanPrincipal").CreditAmount.Should().Be(0.005m);
        operational.Lines.Single(line => line.AccountCode == "PrepaidInterest").CreditAmount.Should().Be(0.005m);
    }

    [DirectLendingDatabaseFact]
    public async Task FractionalFee_PreservesAssessmentReceiptAndAggregateReceivablePrecision()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        await db.Service.AssessFeeAsync(loan.LoanId, new AssessFeeRequest("Synthetic contractual fee", 0.1234m, Origination.AddDays(1), "fractional precision"));
        var assessment = (await db.Store.GetFeeBalancesAsync(loan.LoanId)).Single();
        assessment.OriginalAmount.Should().Be(0.1234m);
        assessment.UnpaidAmount.Should().Be(0.1234m);
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances.FeesAccruedUnpaid.Should().Be(0.1234m);
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(0.1234m, Origination.AddDays(2), new PaymentBreakdownDto(0m, 0m, 0.1234m, 0m, 0m), "fee-collection"));
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances.FeesAccruedUnpaid.Should().Be(0m);
        (await db.Store.GetPaymentAllocationsAsync(loan.LoanId)).Single().AllocatedAmount.Should().Be(0.1234m);
        using var journal = JsonDocument.Parse((await db.GetLedgerEvidenceAsync(loan.LoanId, "loan.mixed-payment-applied")).Single());
        var lines = journal.RootElement.GetProperty("entry").GetProperty("lines").EnumerateArray().ToArray();
        lines.Single(line => line.GetProperty("account").GetProperty("name").GetString() == "FeeReceivable")
            .GetProperty("credit").GetDecimal().Should().Be(0.1234m);
        lines.Should().NotContain(line => line.GetProperty("account").GetProperty("name").GetString() == "FeeIncome");
    }

    [DirectLendingDatabaseFact]
    public async Task MixedPaymentRetry_UsesOriginalRequestAfterTermsAndBalancesChange()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-cash-retry", false);
        var request = new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "same-receipt");
        await db.CommandService.ApplyMixedPaymentAsync(loan.LoanId, request, metadata);
        await db.Service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(Origination.AddDays(2)));
        await db.Service.AmendTermsAsync(loan.LoanId, new AmendLoanTermsRequest(loan.CurrentTerms with
        {
            ExcessPaymentPolicy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-amendment/2")
        }, "Explicit prospective election"));
        var before = await CountArtifactsAsync(db, loan.LoanId);
        var expected = (await db.Store.LoadAsync(loan.LoanId))!.Servicing;
        var retry = await db.CommandService.ApplyMixedPaymentAsync(loan.LoanId, request, metadata);
        retry.Value.Should().BeEquivalentTo(expected);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        Func<Task> changed = () => db.CommandService.ApplyMixedPaymentAsync(loan.LoanId, request with { Amount = 101m }, metadata);
        (await changed.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Conflict);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
    }

    [DirectLendingDatabaseFact]
    public async Task ConcurrentMixedPaymentRetries_CommitOneReceiptAllocationAndJournal()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "synthetic-concurrent-cash", false);
        var request = new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "concurrent-receipt");
        var deliveries = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => db.CommandService.ApplyMixedPaymentAsync(loan.LoanId, request, metadata)));
        deliveries.Should().OnlyContain(result => result.IsSuccess && result.Value!.Balances.UnappliedCash == 100m);
        (await db.Store.GetHistoryAsync(loan.LoanId)).Count(item => item.CommandId == metadata.CommandId).Should().Be(1);
        (await db.Store.GetCashTransactionsAsync(loan.LoanId)).Should().ContainSingle();
        (await db.Store.GetPaymentAllocationsAsync(loan.LoanId)).Should().ContainSingle();
        (await db.GetLedgerEvidenceAsync(loan.LoanId, "loan.mixed-payment-applied")).Should().ContainSingle();
    }

    [DirectLendingDatabaseFact]
    public async Task SnapshotFailure_RollsBackCashAllocationsBalancesJournalAndOutbox()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var before = await CountArtifactsAsync(db, loan.LoanId);
        var state = (await db.Store.LoadAsync(loan.LoanId))!;
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            create function {db.Schema}.reject_cash_snapshot() returns trigger language plpgsql as $test$
            begin raise exception 'Injected failure after ledger append'; end $test$;
            create trigger reject_cash_snapshot before insert on {db.Schema}.loan_snapshot
                for each row execute function {db.Schema}.reject_cash_snapshot();
            """;
        await command.ExecuteNonQueryAsync();
        Func<Task> apply = () => db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "failed-receipt"));
        await apply.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        (await db.Store.LoadAsync(loan.LoanId)).Should().BeEquivalentTo(state);
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances.UnappliedCash.Should().Be(0m);
    }

    [DirectLendingDatabaseFact]
    public async Task MissingPostingPeriod_RejectsReceiptBeforeAnyDurableCashMutation()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var before = await CountArtifactsAsync(db, loan.LoanId);
        Func<Task> apply = () => db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(100m, new DateOnly(2027, 1, 1), null, "no-open-period"));
        (await apply.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        (await db.Store.LoadAsync(loan.LoanId))!.Servicing.Balances.UnappliedCash.Should().Be(0m);
    }

    [DirectLendingDatabaseFact]
    public async Task PrincipalOnlyPath_RejectsForbiddenPrepaymentAndExcessWithoutDiscardingCash()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db, prepaymentAllowed: false);
        var before = await CountArtifactsAsync(db, loan.LoanId);
        var forbidden = await db.CommandService.ApplyPrincipalPaymentAsync(loan.LoanId, new ApplyPrincipalPaymentRequest(100m, Origination.AddDays(1), null));
        forbidden.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        var excess = await db.CommandService.ApplyPrincipalPaymentAsync(loan.LoanId, new ApplyPrincipalPaymentRequest(1_001m, Origination.AddDays(1), null));
        excess.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        var held = await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), new PaymentBreakdownDto(0m, 0m, 0m, 0m, 100m), "prohibited-held"));
        held!.Balances.PrincipalOutstanding.Should().Be(1_000m);
        held.Balances.UnappliedCash.Should().Be(100m);
    }

    [DirectLendingDatabaseFact]
    public async Task StaleReplay_CannotOverwriteALaterCommittedReceipt()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        var stale = (await db.Store.LoadAsync(loan.LoanId))!;
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId, new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "newer-cash"));
        Func<Task> replay = () => db.Store.SaveStateAsync(loan.LoanId, stale.AggregateVersion, stale.Contract, stale.Servicing);
        (await replay.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.ConcurrencyConflict);
        (await db.Store.LoadAsync(loan.LoanId))!.Servicing.Balances.UnappliedCash.Should().Be(100m);
    }

    [DirectLendingDatabaseFact]
    public async Task Migration_UpgradesPopulatedThrough009WithoutChangingLegacyEconomics()
    {
        await using var source = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync(throughLegacyMigration009: true);
        if (source is null || db is null)
            return;
        var loan = await CreateDrawnLoanAsync(source, drawAmount: 1_000.12m);
        var legacy = (await source.Store.LoadAsync(loan.LoanId))!;
        JsonSerializer.Serialize(legacy.Contract, JsonOptions).Should().NotContain("excessPaymentPolicy");
        JsonSerializer.Serialize(legacy.Servicing, JsonOptions).Should().NotContain("unappliedCash").And.NotContain("prepaidInterest");

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("loan_id", loan.LoanId);
        command.Parameters.AddWithValue("schema", db.Schema);
        command.CommandText = """
            select count(*) from information_schema.columns where table_schema = @schema
              and column_name in ('terms_json', 'unapplied_cash', 'prepaid_interest', 'prepaid_interest_lots_json');
            """;
        (await command.ExecuteScalarAsync()).Should().Be(0L, "the target must actually be a through-009 schema before it is populated");
        await CopyLegacyAggregateRowsAsync(source, db, loan.LoanId);
        var before = await CountArtifactsAsync(db, loan.LoanId);
        command.CommandText = $"select contract_json::text from {db.Schema}.loan_state where loan_id = @loan_id;";
        var legacyContractJson = (string)(await command.ExecuteScalarAsync())!;
        command.CommandText = $"select jsonb_agg(to_jsonb(m) order by filename)::text from {db.Schema}.direct_lending_schema_migrations m;";
        var legacyMigrationReceipts = (string)(await command.ExecuteScalarAsync())!;
        command.CommandText = $"select array[current_commitment, total_drawn, available_to_draw, principal_outstanding] from {db.Schema}.loan_servicing_projection where loan_id = @loan_id;";
        var legacyAmounts = (decimal[])(await command.ExecuteScalarAsync())!;
        legacyAmounts.Should().Equal(10_000m, 1_000.12m, 8_999.88m, 1_000.12m);

        await new DirectLendingMigrationRunner(db.Options).EnsureMigratedAsync();
        command.CommandText = $"select array[current_commitment, total_drawn, available_to_draw, principal_outstanding] from {db.Schema}.loan_servicing_projection where loan_id = @loan_id;";
        ((decimal[])(await command.ExecuteScalarAsync())!).Should().Equal(legacyAmounts);
        command.CommandText = $"select contract_json::text from {db.Schema}.loan_state where loan_id = @loan_id;";
        (await command.ExecuteScalarAsync()).Should().Be(legacyContractJson, "the migration must not rewrite contractual operands or hashes");
        command.CommandText = $"select jsonb_agg(to_jsonb(m) order by filename)::text from {db.Schema}.direct_lending_schema_migrations m where filename <> '010_direct_lending_retained_cash.sql';";
        (await command.ExecuteScalarAsync()).Should().Be(legacyMigrationReceipts);
        command.CommandText = $"select count(*) from {db.Schema}.direct_lending_schema_migrations where filename = '010_direct_lending_retained_cash.sql';";
        (await command.ExecuteScalarAsync()).Should().Be(1L);
        var projected = (await db.Store.LoadContractProjectionAsync(loan.LoanId))!;
        projected.CurrentTerms.Should().Be(legacy.Contract.CurrentTerms);
        projected.CurrentTerms.PurchasePrice.Should().Be(99.123456789m, "full retained terms backfill must include operands absent from the legacy normalized columns");
        projected.TermsVersions.Single().TermsHash.Should().Be(loan.TermsVersions.Single().TermsHash);
        var balances = (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances;
        balances.PrincipalOutstanding.Should().Be(1_000.12m);
        balances.UnappliedCash.Should().Be(0m);
        balances.PrepaidInterest.Should().Be(0m);
        balances.PrepaidInterestLots.Should().BeNull();
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before, "DDL must not invent historical cash or journals");

        await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(0.1234m, Origination.AddDays(1), null, "first-post-upgrade-held-receipt"));
        await db.Service.AmendTermsAsync(loan.LoanId, new AmendLoanTermsRequest(loan.CurrentTerms with
        {
            ExcessPaymentPolicy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest,
                "synthetic-post-upgrade-contractual-election")
        }, "New prospective election after legacy upgrade"));
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId,
            new ApplyMixedPaymentRequest(0.5678m, Origination.AddDays(2), null, "first-post-upgrade-prepaid-receipt"));
        var current = (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!;
        current.Balances.UnappliedCash.Should().Be(0.1234m);
        current.Balances.PrepaidInterest.Should().Be(0.5678m);
        current.Balances.PrincipalOutstanding.Should().Be(1_000.12m);
        current.Balances.PrepaidInterestLots.Should().ContainSingle();
        foreach (var mutation in new[]
        {
            "servicing_json = servicing_json #- '{balances,unappliedCash}'",
            "servicing_json = servicing_json #- '{balances,prepaidInterestLots}'",
            "contract_json = contract_json #- '{currentTerms,excessPaymentPolicy}'"
        })
        {
            command.CommandText = $"update {db.Schema}.loan_state set {mutation} where loan_id = @loan_id;";
            Func<Task> oldWriter = () => command.ExecuteNonQueryAsync();
            await oldWriter.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        }
        var afterActivation = await CountArtifactsAsync(db, loan.LoanId);
        await new DirectLendingMigrationRunner(db.Options).EnsureMigratedAsync();
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(afterActivation);
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances
            .Should().BeEquivalentTo(current.Balances, options => options.WithStrictOrdering());
    }

    [DirectLendingDatabaseFact]
    public async Task Migration_PreservesLegacyHashAndOnlyCopiesAnExactlyRetainedTermsVersion()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var loan = await CreateDrawnLoanAsync(db);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"update {db.Schema}.loan_terms_version set terms_json = null where loan_id = @loan_id;";
        command.Parameters.AddWithValue("loan_id", loan.LoanId);
        await command.ExecuteNonQueryAsync();
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "DirectLending", "Migrations", "010_direct_lending_retained_cash.sql"));
        command.CommandText = migration.Replace("__SCHEMA__", db.Schema, StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync();
        await command.ExecuteNonQueryAsync();
        var projected = await db.Store.LoadContractProjectionAsync(loan.LoanId);
        projected!.CurrentTerms.Should().Be(loan.CurrentTerms);
        projected.CurrentTerms.ExcessPaymentPolicy.Should().BeNull();
        projected.TermsVersions.Single().TermsHash.Should().Be(loan.TermsVersions.Single().TermsHash);
        (await db.Store.GetCashTransactionsAsync(loan.LoanId)).Should().BeEmpty();
        (await db.Store.LoadServicingProjectionAsync(loan.LoanId))!.Balances.UnappliedCash.Should().Be(0m);

        command.CommandText = $"update {db.Schema}.loan_terms_version set terms_json = null, terms_hash = 'mismatched-retained-hash' where loan_id = @loan_id;";
        await command.ExecuteNonQueryAsync();
        command.CommandText = migration.Replace("__SCHEMA__", db.Schema, StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"select terms_json is null from {db.Schema}.loan_terms_version where loan_id = @loan_id;";
        (await command.ExecuteScalarAsync()).Should().Be(true, "a hash mismatch cannot be repaired by assuming which terms were intended");
    }

    [DirectLendingDatabaseFact]
    public async Task RetainedCashGuard_RejectsOldWriterErasureAndAllowsAProspectiveAmendment()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;
        var policy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-retained-election");
        var loan = await CreateDrawnLoanAsync(db, policy);
        await db.Service.ApplyMixedPaymentAsync(loan.LoanId, new ApplyMixedPaymentRequest(100m, Origination.AddDays(1), null, "guarded-cash"));
        var before = await CountArtifactsAsync(db, loan.LoanId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("loan_id", loan.LoanId);
        foreach (var mutation in new[]
        {
            "servicing_json = servicing_json #- '{balances,prepaidInterest}'",
            "servicing_json = jsonb_set(servicing_json, '{balances,prepaidInterest}', '99'::jsonb)",
            "servicing_json = servicing_json #- '{balances,prepaidInterestLots}'",
            "servicing_json = jsonb_set(servicing_json, '{balances,prepaidInterestLots,0,application}', '1'::jsonb)",
            "servicing_json = jsonb_set(servicing_json, '{balances,prepaidInterestLots,0,remainingAmount}', '99'::jsonb)",
            "contract_json = contract_json #- '{termsVersions,0,terms,excessPaymentPolicy}'",
            "contract_json = contract_json #- '{currentTerms,excessPaymentPolicy}'"
        })
        {
            command.CommandText = $"update {db.Schema}.loan_state set {mutation} where loan_id = @loan_id;";
            Func<Task> oldWriter = () => command.ExecuteNonQueryAsync();
            await oldWriter.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        }
        (await CountArtifactsAsync(db, loan.LoanId)).Should().Equal(before);
        (await db.Store.LoadAsync(loan.LoanId))!.Servicing.Balances.PrepaidInterest.Should().Be(100m);

        var retained = (await db.Store.LoadAsync(loan.LoanId))!;
        await db.DeleteLiveStateAsync(loan.LoanId);
        command.CommandText = $$"""
            insert into {{db.Schema}}.loan_state (loan_id, aggregate_version, contract_json, servicing_json, updated_at)
            values (@loan_id, @version, cast(@contract as jsonb),
                    cast(@servicing as jsonb) #- '{balances,prepaidInterest}', now());
            """;
        command.Parameters.AddWithValue("version", retained.AggregateVersion);
        command.Parameters.AddWithValue("contract", JsonSerializer.Serialize(retained.Contract, JsonOptions));
        command.Parameters.AddWithValue("servicing", JsonSerializer.Serialize(retained.Servicing, JsonOptions));
        Func<Task> oldRebuilder = () => command.ExecuteNonQueryAsync();
        await oldRebuilder.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        command.CommandText = $$"""
            insert into {{db.Schema}}.loan_state (loan_id, aggregate_version, contract_json, servicing_json, updated_at)
            values (@loan_id, @version,
                    cast(@contract as jsonb) #- '{termsVersions,0,terms,excessPaymentPolicy}',
                    cast(@servicing as jsonb), now());
            """;
        await oldRebuilder.Should().ThrowAsync<PostgresException>().Where(error => error.SqlState == "P0001");
        (await db.QueryService.RebuildStateFromHistoryAsync(loan.LoanId))!.Servicing.Balances.PrepaidInterest.Should().Be(100m);

        var currencyChange = await db.CommandService.AmendTermsAsync(loan.LoanId,
            new AmendLoanTermsRequest(loan.CurrentTerms with { BaseCurrency = CurrencyCode.EUR }, "Currency reinterpretation is not authorized"));
        currencyChange.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        var amended = await db.Service.AmendTermsAsync(loan.LoanId,
            new AmendLoanTermsRequest(loan.CurrentTerms with { ExcessPaymentPolicy = null }, "Prospective removal preserves the historical election"));
        amended!.CurrentTermsVersion.Should().Be(2);
        amended.TermsVersions.Single(version => version.VersionNumber == 1).Terms.ExcessPaymentPolicy.Should().Be(policy);
        (await db.Store.LoadAsync(loan.LoanId))!.Servicing.Balances.PrepaidInterest.Should().Be(100m);
    }

    private static async Task<LoanContractDetailDto> CreateDrawnLoanAsync(DirectLendingPostgresTestDatabase db,
        DirectLendingExcessPaymentPolicyDto? policy = null, bool prepaymentAllowed = true, decimal drawAmount = 1_000m)
    {
        var terms = new DirectLendingTermsDto(Origination, Origination.AddYears(3), 10_000m,
            CurrencyCode.USD, RateTypeKind.Fixed, 0.08m, null, null, null, null,
            DayCountBasis.Act360, PaymentFrequency.Quarterly, AmortizationType.InterestOnly,
            null, null, prepaymentAllowed, null, InterestOnlyMonths: 12, PurchasePrice: 99.123456789m,
            SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                DirectLendingPostgresTestDatabase.TestSecurityId, DirectLendingPostgresTestDatabase.TestSecuritySymbol,
                "synthetic-security", "synthetic-approval", "synthetic-ledger-map"), ExcessPaymentPolicy: policy);
        var loan = await db.Service.CreateLoanAsync(new CreateLoanRequest(Guid.NewGuid(), "Synthetic retained cash loan",
            new BorrowerInfoDto(Guid.NewGuid(), "Synthetic Borrower", null), Origination, terms));
        await db.Service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(Origination));
        await db.Service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(drawAmount, Origination, Origination, "draw"));
        return loan;
    }

    private static async Task CopyLegacyAggregateRowsAsync(
        DirectLendingPostgresTestDatabase source, DirectLendingPostgresTestDatabase target, Guid loanId)
    {
        await using var sourceConnection = new NpgsqlConnection(source.ConnectionString);
        await using var targetConnection = new NpgsqlConnection(target.ConnectionString);
        await sourceConnection.OpenAsync();
        await targetConnection.OpenAsync();
        await using var transaction = await targetConnection.BeginTransactionAsync();
        // These create/activate/draw records retain the unchanged legacy JSON shape.
        // Populate actual pre-010 table types: later normalized columns are ignored,
        // so no new balance or full-terms columns exist until the forward migration.
        foreach (var table in new[] { "loan_state", "loan_event", "loan_contract", "loan_terms_version",
                     "loan_servicing_projection", "drawdown_lot_projection", "servicing_revision_projection", "loan_snapshot" })
        {
            await using var read = sourceConnection.CreateCommand();
            read.CommandText = $"select to_jsonb(retained)::text from {source.Schema}.{table} retained where loan_id = @loan_id;";
            read.Parameters.AddWithValue("loan_id", loanId);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                await using var insert = targetConnection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"insert into {target.Schema}.{table} select * from jsonb_populate_record(null::{target.Schema}.{table}, cast(@retained as jsonb));";
                insert.Parameters.AddWithValue("retained", reader.GetString(0));
                await insert.ExecuteNonQueryAsync();
            }
        }
        await using var sequence = targetConnection.CreateCommand();
        sequence.Transaction = transaction;
        // Migration 005_workflows changed the default to this explicit sequence;
        // the older serial-owned sequence is no longer the event allocator.
        sequence.CommandText = $"select setval('{target.Schema}.loan_event_position_seq', (select max(event_position) from {target.Schema}.loan_event), true);";
        await sequence.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static void AssertReceiptJournal(string payload, string liabilityAccount, decimal amount)
    {
        using var document = JsonDocument.Parse(payload);
        var lines = document.RootElement.GetProperty("entry").GetProperty("lines").EnumerateArray().ToArray();
        lines.Should().HaveCount(2);
        lines.Sum(line => line.GetProperty("debit").GetDecimal()).Should().Be(amount);
        lines.Sum(line => line.GetProperty("credit").GetDecimal()).Should().Be(amount);
        lines.Single(line => line.GetProperty("account").GetProperty("name").GetString() == "Cash").GetProperty("debit").GetDecimal().Should().Be(amount);
        lines.Single(line => line.GetProperty("account").GetProperty("name").GetString() == liabilityAccount).GetProperty("credit").GetDecimal().Should().Be(amount);
    }

    private static async Task<long[]> CountArtifactsAsync(DirectLendingPostgresTestDatabase db, Guid loanId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        var results = new List<long>();
        foreach (var table in new[] { "loan_event", "cash_transaction", "payment_allocation", "test_ledger_evidence", "loan_snapshot" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"select count(*) from {db.Schema}.{table} where loan_id = @loan_id;";
            command.Parameters.AddWithValue("loan_id", loanId);
            results.Add((long)(await command.ExecuteScalarAsync())!);
        }
        await using var outbox = connection.CreateCommand();
        outbox.CommandText = $"select count(*) from {db.Schema}.outbox_message where payload ->> 'loanId' = @loan_id;";
        outbox.Parameters.AddWithValue("loan_id", loanId.ToString("D"));
        results.Add((long)(await outbox.ExecuteScalarAsync())!);
        return results.ToArray();
    }
}
