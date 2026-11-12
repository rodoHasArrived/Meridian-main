using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Meridian.TestSupport;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class IntercompanyAgreementStorePostgresTests
{
    private const string Tenant = "tenant-a";
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    [FundAccountDatabaseFact]
    public async Task Reload_RetainsImmutableFactsAndPartitionsEveryReadByTenant()
    {
        await using var data = await Fixture.CreateAsync();
        var first = Initial();
        await SaveAsync(data.Store, first);
        var next = Next(first);
        await SaveAsync(data.Store, next);

        var reloaded = Assert.IsType<IntercompanyAgreementStoreState>(await data.Store.GetAsync(" TENANT-A ", first.AgreementId));
        Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(reloaded));
        Assert.Null(await data.Store.GetAsync("tenant-b", first.AgreementId));
        Assert.Empty(await data.Store.ListAsync("tenant-b"));
        Assert.Single(await data.Store.ListAsync(Tenant));
        Assert.Equal(2, await data.CountAsync("intercompany_agreement_snapshot"));
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_revision"));
        Assert.Equal(2, await data.CountAsync("intercompany_agreement_audit"));
        Assert.Equal(2, await data.CountAsync("intercompany_agreement_command_receipt"));
    }

    [FundAccountDatabaseFact]
    public async Task ConcurrentStores_OnlyOneCommandCanAdvanceTheSameExpectedHead()
    {
        await using var data = await Fixture.CreateAsync();
        var first = Initial();
        await SaveAsync(data.Store, first);
        var candidates = new[] { Next(first), Next(first) };
        var outcomes = await Task.WhenAll(candidates.Select(async candidate =>
        {
            try { await SaveAsync(data.Store, candidate); return true; }
            catch (IntercompanyAgreementConflictException) { return false; }
        }));

        Assert.Single(outcomes, value => value);
        Assert.Single(outcomes, value => !value);
        Assert.Equal(2, (await data.Store.GetAsync(Tenant, first.AgreementId))!.AggregateVersion);
        Assert.Equal(2, await data.CountAsync("intercompany_agreement_command_receipt"));
    }

    [FundAccountDatabaseFact]
    public async Task CommandIdentity_CannotBeReusedForAnotherAgreementWithinTheTenant()
    {
        await using var data = await Fixture.CreateAsync();
        var first = Initial();
        await SaveAsync(data.Store, first);
        var second = Initial();
        second = second with { Receipts = [second.Receipts[0] with { CommandId = first.Receipts[0].CommandId }] };
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => SaveAsync(data.Store, second));
        Assert.Null(await data.Store.GetAsync(Tenant, second.AgreementId));
        Assert.Single(await data.Store.ListAsync(Tenant));
    }

    [FundAccountDatabaseFact]
    public async Task FailedCallbackAndHistoricalRewrite_RollBackWithoutAdvancingTheHead()
    {
        await using var data = await Fixture.CreateAsync();
        var first = Initial();
        await SaveAsync(data.Store, first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Store.ExecuteAsync<bool>(Tenant, first.AgreementId,
            (unit, _) =>
            {
                unit.Save(Next(first));
                throw new InvalidOperationException("Synthetic failure before commit.");
            }));

        var changedRevision = first.Revisions[0] with { Rationale = "attempted rewrite" };
        var invalid = Next(first) with { Revisions = [changedRevision] };
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => SaveAsync(data.Store, invalid));
        var changedAudit = first.History[0] with { Actor = "different-author" };
        invalid = Next(first) with { History = [changedAudit, Next(first).History[^1]] };
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => SaveAsync(data.Store, invalid));

        Assert.Equal(1, (await data.Store.GetAsync(Tenant, first.AgreementId))!.AggregateVersion);
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_snapshot"));
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_audit"));
    }

    [FundAccountDatabaseFact]
    public async Task DatabaseGuards_RejectHistoricalUpdatesDeletesTruncationAndHeadReassignment()
    {
        await using var data = await Fixture.CreateAsync();
        await SaveAsync(data.Store, Initial());
        foreach (var table in new[] { "intercompany_agreement_snapshot", "intercompany_agreement_revision",
                     "intercompany_agreement_audit", "intercompany_agreement_command_receipt" })
        {
            foreach (var sql in new[] { $"update {data.Q(table)} set payload = payload", $"delete from {data.Q(table)}",
                         $"truncate {data.Q(table)} cascade" })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => data.ExecuteAsync(sql));
                Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            }
        }
        foreach (var sql in new[]
                 {
                     $"update {data.Q("intercompany_agreement")} set tenant_id = 'tenant-b'",
                     $"update {data.Q("intercompany_agreement")} set aggregate_version = aggregate_version + 2",
                     $"delete from {data.Q("intercompany_agreement")}"
                 })
            Assert.Equal(PostgresErrorCodes.RaiseException,
                (await Assert.ThrowsAsync<PostgresException>(() => data.ExecuteAsync(sql))).SqlState);
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_snapshot"));
    }

    [FundAccountDatabaseFact]
    public async Task PostingLease_HoldsAgreementAndEntityAuthorityUntilDisposal()
    {
        await using var data = await Fixture.CreateAsync();
        var first = Initial();
        await SaveAsync(data.Store, first);
        var party = first.Revisions[0].Terms.PartyOneId;
        await data.SeedEntityAsync(party, Tenant);
        var lease = await data.Store.AcquirePostingLeaseAsync(Tenant, [first.AgreementId]);
        try
        {
            Assert.Single(lease.Agreements);
            Assert.NotNull(await lease.GetLegalEntityAsync(party));
            var headError = await Assert.ThrowsAsync<PostgresException>(() => data.ExecuteAsync(
                $"set lock_timeout = '150ms'; update {data.Q("intercompany_agreement")} set aggregate_version = aggregate_version + 1"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, headError.SqlState);
            var entityError = await Assert.ThrowsAsync<PostgresException>(() => data.ExecuteAsync(
                $"set lock_timeout = '150ms'; update {data.Q("legal_entity")} set name = 'changed'"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, entityError.SqlState);
        }
        finally { await lease.DisposeAsync(); }

        await SaveAsync(data.Store, Next(first));
        await data.ExecuteAsync($"update {data.Q("legal_entity")} set name = 'changed'");
        Assert.Equal(2, (await data.Store.GetAsync(Tenant, first.AgreementId))!.AggregateVersion);
    }

    [FundAccountDatabaseFact]
    public async Task EntityAuthority_RejectsForeignAndUnattributedPartiesInsideBothLeaseKinds()
    {
        await using var data = await Fixture.CreateAsync();
        var own = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var unattributed = Guid.NewGuid();
        await data.SeedEntityAsync(own, Tenant);
        await data.SeedEntityAsync(foreign, "tenant-b");
        await data.SeedEntityAsync(unattributed, null);
        await data.Store.ExecuteAsync(Tenant, Guid.NewGuid(), async (unit, ct) =>
        {
            Assert.NotNull(await unit.GetLegalEntityAsync(own, ct));
            Assert.Null(await unit.GetLegalEntityAsync(foreign, ct));
            Assert.Null(await unit.GetLegalEntityAsync(unattributed, ct));
            return true;
        });
        await using var lease = await data.Store.AcquirePostingLeaseAsync(Tenant, []);
        Assert.NotNull(await lease.GetLegalEntityAsync(own));
        Assert.Null(await lease.GetLegalEntityAsync(foreign));
        Assert.Null(await lease.GetLegalEntityAsync(unattributed));
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => data.Store.ListAsync(" "));
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => data.Store.ListAsync(" ALL "));
    }

    private static Task<bool> SaveAsync(PostgresIntercompanyAgreementStore store, IntercompanyAgreementStoreState state) =>
        store.ExecuteAsync(Tenant, state.AgreementId, (unit, _) =>
        {
            unit.Save(state);
            return Task.FromResult(true);
        });

    private static IntercompanyAgreementStoreState Initial()
    {
        var id = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var draft = new IntercompanyAgreementDraftDto("IC-001", "Synthetic bilateral loan", Guid.NewGuid(), "Lender",
            Guid.NewGuid(), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new DateOnly(2026, 1, 1),
            Terms: new(Notes: "Synthetic fixed agreement terms"));
        var proposal = new IntercompanyAgreementProposalDto(proposalId, IntercompanyAgreementActionDto.Activate,
            IntercompanyAgreementProposalStateDto.Approved, draft, null, ["maker"], "maker", Now, "reviewed-hash",
            Now, Now, "Synthetic approval");
        var revision = new IntercompanyAgreementRevisionDto(id, 1, null, null, IntercompanyAgreementActionDto.Activate,
            draft, proposalId, "reviewed-hash", ["maker"], "maker", "reviewer", Now, "Synthetic approval");
        var history = new[] { new IntercompanyAgreementAuditDto(Guid.NewGuid(), proposalId, "Approve", "reviewer", Now, 1,
            "Synthetic approval", draft) };
        var detail = Detail(id, 1, draft, [proposal], [revision], history);
        var receipt = new IntercompanyAgreementCommandReceiptDto(Guid.NewGuid(), "Approve", "command-hash", "reviewer", Now, 1, detail);
        return new(id, 1, [proposal], [revision], history, [receipt]);
    }

    private static IntercompanyAgreementStoreState Next(IntercompanyAgreementStoreState previous)
    {
        var version = previous.AggregateVersion + 1;
        var history = previous.History.Append(new IntercompanyAgreementAuditDto(Guid.NewGuid(), previous.Proposals[0].ProposalId,
            "RetainReview", "reviewer", Now.AddSeconds(version), version, "Synthetic subsequent command")).ToArray();
        var detail = Detail(previous.AgreementId, version, previous.Revisions[0].Terms, previous.Proposals, previous.Revisions, history);
        var receipt = new IntercompanyAgreementCommandReceiptDto(Guid.NewGuid(), "RetainReview", "next-command", "reviewer",
            Now.AddSeconds(version), version, detail);
        return previous with { AggregateVersion = version, History = history, Receipts = [.. previous.Receipts, receipt] };
    }

    private static IntercompanyAgreementDetailDto Detail(Guid id, long version, IntercompanyAgreementDraftDto draft,
        IReadOnlyList<IntercompanyAgreementProposalDto> proposals, IReadOnlyList<IntercompanyAgreementRevisionDto> revisions,
        IReadOnlyList<IntercompanyAgreementAuditDto> history) => new(id, version, draft.Code, draft.Name,
        draft.PartyOneId, draft.PartyOneRole, draft.PartyTwoId, draft.PartyTwoRole, draft.AgreementType, draft.Currency,
        new DateOnly(2026, 11, 12), IntercompanyAgreementStatusDto.Active, revisions.Count, revisions[^1], proposals,
        revisions, history, new(false, false, false, false, false, true, true));

    private sealed class Fixture(PostgresTestServer server, FundStructureStoreOptions options) : IAsyncDisposable
    {
        public PostgresIntercompanyAgreementStore Store => new(options);
        public string Q(string name) => $"\"{options.Schema}\".\"{name}\"";

        public static async Task<Fixture> CreateAsync()
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING");
            var options = new FundStructureStoreOptions
            {
                ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("ic_registry")
            };
            try
            {
                await new FundStructureMigrationRunner(options).EnsureMigratedAsync();
                return new(server, options);
            }
            catch { await server.DisposeAsync(); throw; }
        }

        public async Task SeedEntityAsync(Guid id, string? tenant)
        {
            var store = new PostgresFundStructureStore(options);
            await store.UpsertLegalEntityAsync(new(id, LegalEntityTypeDto.Vehicle, id.ToString("N"), "Synthetic entity",
                "US", "USD", true, Now.AddYears(-1), null));
            if (tenant is not null)
                await store.StampNodeTenantAsync(id, tenant);
        }

        public async Task<int> ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<long> CountAsync(string table)
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"select count(*) from {Q(table)}", connection);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
