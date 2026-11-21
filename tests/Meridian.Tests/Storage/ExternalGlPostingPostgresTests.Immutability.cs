using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.AccountingSystem;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class ExternalGlPostingPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ImmutableEvidence_RefusesDirectAndCascadingTruncateWhileNewApprovedRevisionsRemainAppendable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter(Tenant, "external GL immutable evidence synthetic test");
        var (store, _, scope) = await SeedAsync(database, ct);
        var firstJournal = await AddJournalAsync(store, scope, Now, ct);
        using var http = new SyntheticQuickBooks();
        using var client = new HttpClient(http);
        var adapter = Adapter(client);
        var accounting = new AccountingSystemIntegrationService([], store);
        var service = new ExternalGlPostingService(store, accounting, [adapter], TimeProvider.System);
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection,
            false, ApproveActor, Human, ct);
        await RetainMappingAsync(accounting, scope, ct);
        var first = await PrepareAsync(service, scope, [firstJournal.JournalEntryId], ct);
        await ApproveAsync(service, scope, first, ct);
        var dispatcher = new ExternalGlPostingDispatcher(store, [adapter], TimeProvider.System);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        foreach (var table in new[] { "external_gl_mapping_profiles", "external_gl_posting_packages",
                     "external_gl_posting_decisions", "external_gl_posting_receipts" })
        {
            foreach (var cascade in new[] { false, true })
            {
                await using var truncate = connection.CreateCommand();
                truncate.CommandText = $"truncate table \"{database.Options.SchemaName}\".{table}{(cascade ? " cascade" : "")}";
                var action = () => truncate.ExecuteNonQueryAsync(ct);
                var rejection = (await action.Should().ThrowAsync<PostgresException>()).Which;
                if (cascade) rejection.SqlState.Should().Be("55000", "the immutable statement guard must refuse cascading truncation");
                else rejection.SqlState.Should().BeOneOf("55000", "0A000");
            }
        }
        await using (var truncateOutbox = connection.CreateCommand())
        {
            truncateOutbox.CommandText = $"truncate table \"{database.Options.SchemaName}\".external_gl_posting_outbox cascade";
            var action = () => truncateOutbox.ExecuteNonQueryAsync(ct);
            (await action.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        }

        var restarted = Restart(database);
        var unchanged = (await restarted.GetExternalGlPackageAsync(scope, first.PackageId, ct))!;
        unchanged.PayloadHash.Should().Be(first.PayloadHash);
        unchanged.Approval!.PayloadHash.Should().Be(first.PayloadHash);
        unchanged.Status.Should().Be(ExternalGlPostingStatus.FullyAccepted);
        unchanged.Progress.Single().Observations.Should().NotBeEmpty();

        await RetainMappingAsync(accounting, scope, ct, capitalId: "33");
        var secondJournal = await AddJournalAsync(restarted, scope, Now.AddDays(1), ct);
        var second = await PrepareAsync(service, scope, [secondJournal.JournalEntryId], ct);
        second.Payload.Mapping.Version.Should().NotBe(first.Payload.Mapping.Version);
        await ApproveAsync(service, scope, second, ct);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();
        var retainedFirst = (await restarted.GetExternalGlPackageAsync(scope, first.PackageId, ct))!;
        retainedFirst.Payload.Mapping.Version.Should().Be(first.Payload.Mapping.Version);
        (await restarted.GetExternalGlPackageAsync(scope, second.PackageId, ct))!.Status.Should().Be(ExternalGlPostingStatus.FullyAccepted);
        await using var count = connection.CreateCommand();
        count.CommandText = $"select count(*) from \"{database.Options.SchemaName}\".external_gl_mapping_profiles";
        ((long)(await count.ExecuteScalarAsync(ct))!).Should().Be(2);
    }
}
