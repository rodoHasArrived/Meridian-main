using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Npgsql;

namespace Meridian.Tests.Application.DirectLending;

public sealed partial class PostgresDirectLendingCommandServiceTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task RestructureLoanAsync_PostgresRetainsOneHaircutAcrossRetryRestartAndStateRecovery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var at = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var book = new LedgerBookRecord(Guid.NewGuid(), "restructuring-fixture", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Synthetic loan book", "USD", at, at);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 3, "March 2026",
            new(2026, 3, 1), new(2026, 3, 31), "Open", at, null, 0)
        { MutationActor = "fixture-controller" };
        await database.JournalStore.SavePeriodAsync(period, 0, ct: ct);
        var options = new DirectLendingOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.CreateSchemaName("restructure"),
            SnapshotIntervalVersions = 2,
            CurrentEventSchemaVersion = 1
        };
        await new DirectLendingMigrationRunner(options).EnsureMigratedAsync(ct);

        (PostgresDirectLendingStateStore Store, PostgresDirectLendingQueryService Query, PostgresDirectLendingCommandService Commands) Restart()
        {
            // Real transaction-aware journal persistence; the direct-lending suite's no-op
            // journal fixture cannot establish this accounting acceptance test.
            var journals = database.RestartJournalStore();
            var store = new PostgresDirectLendingStateStore(options, journals);
            var query = new PostgresDirectLendingQueryService(store, store, new DirectLendingEventRebuilder());
            return (store, query, new PostgresDirectLendingCommandService(store, store, query,
                new LoanAccountingProjector(journals, new AccountingPolicyService(), BuildSecurityMasterQueryService()), options));
        }

        var services = Restart();
        var loanId = Guid.NewGuid();
        var contract = BuildContract(loanId);
        var created = await services.Commands.CreateLoanAsync(new(loanId, contract.FacilityName,
            contract.Borrower, contract.EffectiveDate, contract.CurrentTerms), ct: ct);
        created.IsSuccess.Should().BeTrue();
        (await services.Commands.ActivateLoanAsync(loanId, new(new(2026, 3, 22)), ct: ct)).IsSuccess.Should().BeTrue();
        (await services.Commands.BookDrawdownAsync(loanId, new(100m, new(2026, 3, 22), new(2026, 3, 22), "synthetic-wire"), ct: ct))
            .IsSuccess.Should().BeTrue();
        var funded = (await services.Store.LoadAsync(loanId, ct))!;
        var request = new RestructureLoanRequest(RestructuringType.PrincipalHaircut, "Approved principal concession",
            new(2026, 3, 24), contract.CurrentTerms with { FixedAnnualRate = 0.04m }, 25m);
        var metadata = new DirectLendingCommandMetadataDto(Guid.NewGuid(), null, null, "restructuring-test", false);

        var invalid = await services.Commands.RestructureLoanAsync(loanId, request with { HaircutAmount = 150m }, metadata, ct);
        invalid.Error!.Code.Should().Be(DirectLendingErrorCode.Validation);
        (await services.Store.LoadAsync(loanId, ct)).Should().BeEquivalentTo(funded);
        (await database.JournalStore.GetByAggregateAsync(loanId, ct)).Should().ContainSingle();

        (await services.Commands.RestructureLoanAsync(loanId, request, metadata, ct)).IsSuccess.Should().BeTrue();
        var partial = (await services.Store.LoadAsync(loanId, ct))!;
        partial.Servicing.Balances.PrincipalOutstanding.Should().Be(75m);
        partial.Servicing.DrawdownLots.Sum(lot => lot.RemainingPrincipal).Should().Be(75m);
        var fullRequest = request with { HaircutAmount = 75m, NewTerms = null, EffectiveDate = new(2026, 3, 25) };
        var fullMetadata = metadata with { CommandId = Guid.NewGuid() };
        (await services.Commands.RestructureLoanAsync(loanId, fullRequest, fullMetadata, ct)).IsSuccess.Should().BeTrue();
        var committed = (await services.Store.LoadAsync(loanId, ct))!;
        var history = await services.Store.GetHistoryAsync(loanId, ct);
        var journalsBeforeRetry = await database.JournalStore.GetByAggregateAsync(loanId, ct);
        var losses = journalsBeforeRetry.Where(journal => journal.Entry.Metadata?.ActivityType == "loan.restructured").ToArray();
        losses.Should().HaveCount(2);
        losses.Should().OnlyContain(journal => journal.Entry.IsBalanced && journal.SourceEventId != null);
        losses.SelectMany(journal => journal.Entry.Lines).Where(line => line.Account.Name == "LoanPrincipal")
            .Sum(line => line.Credit).Should().Be(100m);
        losses.SelectMany(journal => journal.Entry.Lines).Where(line => line.Account.Name == "RestructuringLoss")
            .Sum(line => line.Debit).Should().Be(100m);
        history.Where(entry => entry.EventType == "loan.restructured").Select(entry => entry.EventId)
            .Should().BeEquivalentTo(losses.Select(journal => journal.SourceEventId!.Value));

        services = Restart();
        (await services.Commands.RestructureLoanAsync(loanId, fullRequest, fullMetadata, ct)).IsSuccess.Should().BeTrue();
        (await services.Commands.RestructureLoanAsync(loanId, request, metadata, ct)).IsSuccess.Should().BeTrue();
        (await services.Store.LoadAsync(loanId, ct)).Should().BeEquivalentTo(committed);
        (await services.Store.GetHistoryAsync(loanId, ct)).Should().BeEquivalentTo(history);
        (await database.RestartJournalStore().GetByAggregateAsync(loanId, ct)).Should().BeEquivalentTo(journalsBeforeRetry);

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var sql = connection.CreateCommand();
        sql.CommandText = $"select count(*) from {options.Schema}.outbox_message where payload->>'loanId' = @loan_id and payload->>'eventType' = 'loan.restructured';";
        sql.Parameters.AddWithValue("loan_id", loanId.ToString("D"));
        (await sql.ExecuteScalarAsync(ct)).Should().Be(6L, "each of two committed restructurings owns three publications, regardless of retries");
        foreach (var entry in history.Where(entry => entry.EventType == "loan.restructured"))
        {
            sql.CommandText = $"select count(distinct topic) from {options.Schema}.outbox_message where payload->>'loanId' = @loan_id and message_key = @event_key and payload->>'sourceEventId' = @event_id;";
            sql.Parameters.AddWithValue("event_key", $"{loanId:D}/{entry.EventId:D}");
            sql.Parameters.AddWithValue("event_id", entry.EventId.ToString("D"));
            (await sql.ExecuteScalarAsync(ct)).Should().Be(3L);
            sql.Parameters.RemoveAt("event_key");
            sql.Parameters.RemoveAt("event_id");
        }
        sql.Parameters.Clear();
        sql.Parameters.AddWithValue("loan_id", loanId);
        sql.CommandText = $"delete from {options.Schema}.loan_state where loan_id = @loan_id;";
        (await sql.ExecuteNonQueryAsync(ct)).Should().Be(1);

        services = Restart();
        var recovered = (await services.Query.LoadAggregateAsync(loanId, ct))!;
        recovered.AggregateVersion.Should().Be(committed.AggregateVersion);
        recovered.Contract.Status.Should().Be(LoanStatus.Workout);
        recovered.Contract.CurrentTerms.Should().Be(committed.Contract.CurrentTerms);
        recovered.Contract.CurrentTermsVersion.Should().Be(committed.Contract.CurrentTermsVersion);
        recovered.Contract.TermsVersions.Single(version => version.VersionNumber == 2)
            .Should().Be(committed.Contract.TermsVersions.Single(version => version.VersionNumber == 2));
        recovered.Servicing.Balances.Should().Be(committed.Servicing.Balances);
        recovered.Servicing.DrawdownLots.Should().BeEquivalentTo(committed.Servicing.DrawdownLots);
        recovered.Servicing.ServicingRevision.Should().Be(committed.Servicing.ServicingRevision);
        recovered.Servicing.RevisionHistory.Where(revision => revision.RevisionSourceType == "Restructuring")
            .Should().Equal(committed.Servicing.RevisionHistory.Where(revision => revision.RevisionSourceType == "Restructuring"));
        var rebuiltAgain = await services.Query.RebuildStateFromHistoryAsync(loanId, ct);
        JsonSerializer.Serialize(rebuiltAgain!.Servicing).Should().Be(JsonSerializer.Serialize(recovered.Servicing));
        (await services.Store.GetHistoryAsync(loanId, ct)).Should().BeEquivalentTo(history);
        (await database.RestartJournalStore().GetByAggregateAsync(loanId, ct)).Should().BeEquivalentTo(journalsBeforeRetry);
    }
}
