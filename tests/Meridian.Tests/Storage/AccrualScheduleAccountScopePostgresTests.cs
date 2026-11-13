using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class AccrualScheduleAccountScopePostgresTests
{
    [LedgerDatabaseFact]
    public async Task AccountAndSleeveBooks_ReviewedAccrualAccountPassesRealPeriodAndSubjectAuthority()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var structureOptions = new FundStructureStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.CreateSchemaName("accrual_structure")
        };
        var accountOptions = new FundAccountStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.CreateSchemaName("accrual_accounts")
        };
        try
        {
            await new FundStructureMigrationRunner(structureOptions).EnsureMigratedAsync(ct);
            await new FundAccountMigrationRunner(accountOptions).EnsureMigratedAsync(ct);
            var structures = new PostgresFundStructureStore(structureOptions);
            var accounts = new PostgresFundAccountStore(accountOptions);
            var tenancy = new PostgresFundProfileTenancyRegistry(database.Options);
            var now = DateTimeOffset.UtcNow;
            var entityId = Guid.NewGuid();
            var fundId = Guid.NewGuid();
            var sleeveId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            const string tenant = "accrual-scope-tenant";
            const string company = "accrual-scope-company";
            const string fund = "accrual-scope-fund";
            using var tenantScope = FundScopeTenantAuthority.Enter(tenant, "accrual-account-scope-fixture");
            await tenancy.BindAsync(fund, tenant, company, ct);
            await structures.UpsertLegalEntityAsync(new(entityId, LegalEntityTypeDto.Fund, "ENTITY", "Accrual entity", "US", "USD", true, now.AddYears(-1), null), ct);
            await structures.UpsertFundAsync(new(fundId, null, "FUND", "Accrual fund", "USD", true, now.AddYears(-1), null,
                [sleeveId], [], [entityId], [], [accountId]), ct);
            await structures.UpsertSleeveAsync(new(sleeveId, fundId, "SLEEVE", "Accrual sleeve", null, true,
                now.AddYears(-1), null, [], [], [accountId]), ct);
            await accounts.UpsertAccountAsync(new(accountId, AccountTypeDto.Bank, entityId, fundId, sleeveId, null,
                "ACCOUNT", "Accrual account", "USD", "Synthetic bank", true, now.AddYears(-1), null, null, null, null, null), ct);
            // The account definition and its structure-node link are separate retained records.
            // Stamping alone only updates existing nodes; it cannot create this membership.
            await structures.UpsertLinkedAccountIdAsync(accountId, ct);
            foreach (var node in new[] { entityId, fundId, sleeveId, accountId })
                await structures.StampNodeTenantAsync(node, tenant, ct);
            var retainedTenants = await structures.GetNodeTenantsAsync(ct);
            retainedTenants.NodeTenants.Should().ContainKey(accountId).WhoseValue.Should().Be(tenant);
            var authority = new RecurringJournalPeriodAuthority(database.JournalStore, tenancy,
                new RecurringJournalSubjectAuthority(structures, accounts));
            foreach (var ownerKind in new[] { FundStructureNodeKindDto.Account, FundStructureNodeKindDto.Sleeve })
            {
                var book = new LedgerBookRecord(Guid.NewGuid(), fund,
                    ownerKind == FundStructureNodeKindDto.Account ? accountId : sleeveId, ownerKind,
                    "Scoped accrual book", "USD", now, now);
                await database.JournalStore.SaveLedgerBookAsync(book, ct);
                var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId,
                    2026, 10, "October", new(2026, 10, 1), new(2026, 10, 31), "Open", now, null, 0)
                    { MutationActor = "period-controller" }, 0, ct: ct);
                var input = new AccrualScheduleInput("scoped-fee", fund, book.LedgerBookId, entityId.ToString("D"), "USD",
                    RecurringJournalCadence.Monthly, new(2026, 10, 1), new(2026, 10, 1), "125.00", "Expense:Fees", LedgerAccountType.Expense,
                    "Liability:Accruals", LedgerAccountType.Liability, "Reviewed accrual", [new("source", "retained://fees", "Accrual", "Terms", now, "custodian", EvidenceVersion: 1)],
                    FundAccountId: accountId.ToString("D"));
                var prepared = LedgerEndpoints.BuildAccrualSchedule(input, tenant, company);
                var resolved = await authority.ResolveAsync(prepared.Scope, input.AnchorDate, ct);
                resolved.IsOpen.Should().BeTrue();
                resolved.PeriodId.Should().Be(period.PeriodId.ToString("D"));
                prepared.Scope.FundAccountId.Should().Be(accountId.ToString("D"));
                prepared.Schedule.Dimensions!.AccountId.Should().Be(accountId.ToString("D"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ResolveAsync(
                    prepared.Scope with { FundAccountId = null }, input.AnchorDate, ct));
                await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ResolveAsync(
                    prepared.Scope with { FundAccountId = Guid.NewGuid().ToString("D") }, input.AnchorDate, ct));
                (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty("scope preparation never posts a journal");
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {accountOptions.Schema} CASCADE; DROP SCHEMA IF EXISTS {structureOptions.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
