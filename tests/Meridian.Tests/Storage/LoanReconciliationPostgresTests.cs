using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class LoanReconciliationPostgresTests
{
    [LedgerDatabaseFact]
    public async Task Migration12_RestartPreservesRequestAndSourceEvidenceAndRejectsChangedRetry()
    {
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING");
        var options = new DirectLendingOptions
        { ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("loan_recon") };
        await new DirectLendingMigrationRunner(options).EnsureMigratedAsync();
        var loanId = Guid.NewGuid();
        var projectionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = $"""
                insert into "{options.Schema}".loan_contract
                    (loan_id, aggregate_version, facility_name, borrower_id, borrower_name, status,
                     effective_date, current_terms_version, base_currency)
                values (@loan, 1, 'Synthetic loan', @borrower, 'Synthetic borrower', 'Active', date '2026-01-01', 1, 'USD');
                insert into "{options.Schema}".loan_event
                    (event_id, loan_id, aggregate_version, event_type, payload)
                values (@event, @loan, 1, 'loan.created', jsonb_build_object());
                insert into "{options.Schema}".projection_run
                    (projection_run_id, loan_id, loan_terms_version, servicing_revision, projection_as_of,
                     trigger_event_id, trigger_type, terms_hash, engine_version, status)
                values (@projection, @loan, 1, 1, date '2026-11-21', @event, 'Synthetic', 'synthetic-hash', 'synthetic-v1', 'Completed');
                """;
            seed.Parameters.AddWithValue("loan", loanId);
            seed.Parameters.AddWithValue("borrower", Guid.NewGuid());
            seed.Parameters.AddWithValue("event", eventId);
            seed.Parameters.AddWithValue("projection", projectionId);
            await seed.ExecuteNonQueryAsync();
        }
        var store = new PostgresDirectLendingStateStore(options);
        var now = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
        var request = new ReconcileLoanRequest(0.01m, 1);
        var run = new ReconciliationRunDto(Guid.NewGuid(), loanId, projectionId, now, now, "Completed")
        { RequestHash = LoanCashReconciliation.RequestHash(request), AsOfDate = new(2026, 11, 21), AmountTolerance = 0.01m, DateToleranceDays = 1 };
        var row = new ReconciliationResultDto(Guid.NewGuid(), run.ReconciliationRunId, loanId, null, null,
            "LedgerBalanceMismatch", 100m, 99m, -1m, run.AsOfDate, run.AsOfDate, "CanonicalRole:LoanPrincipal", null, ["Synthetic mismatch"], now)
        {
            MeasurementKind = "ledger-balance:LoanPrincipal",
            Currency = "USD",
            AsOfDate = run.AsOfDate,
            LedgerBookId = Guid.NewGuid(),
            ServicingRevision = 1,
            JournalEntryIds = [Guid.NewGuid()]
        };
        var exception = new ReconciliationExceptionDto(Guid.NewGuid(), row.ReconciliationResultId,
            "LedgerBalanceMismatch", "High", "Resolved", "synthetic-owner", "Synthetic explanation", now, now);
        await store.SaveReconciliationRunAsync(run, [row], [exception]);
        var restarted = new PostgresDirectLendingStateStore(options);
        (await restarted.GetReconciliationRunsAsync(loanId)).Should().ContainSingle().Which.Should().Be(run);
        (await restarted.GetReconciliationResultsAsync(run.ReconciliationRunId)).Should().ContainSingle().Which.Should().BeEquivalentTo(row);
        await restarted.SaveReconciliationRunAsync(run, [], []);
        (await restarted.GetReconciliationExceptionsAsync()).Should().ContainSingle().Which.Status.Should().Be("Resolved");
        var conflict = () => restarted.SaveReconciliationRunAsync(run with
        { RequestHash = LoanCashReconciliation.RequestHash(new(0.02m, 1)), AmountTolerance = 0.02m }, [], []);
        (await conflict.Should().ThrowAsync<DirectLendingCommandException>()).Which.Error.Code.Should().Be(DirectLendingErrorCode.ConcurrencyConflict);
    }
}
