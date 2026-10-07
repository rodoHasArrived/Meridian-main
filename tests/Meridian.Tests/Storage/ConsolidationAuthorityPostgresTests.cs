using System.Data;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;
using Npgsql;
using CoreFixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class ConsolidationAuthorityPostgresTests
{
    [LedgerDatabaseFact]
    public async Task DirectCorrectionAppend_RequiresAdjustmentClassificationAndRetainsApprovedLineage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await AuthorityFixture.CreateAsync(ct);
        await fixture.Store.AppendAsync(fixture.Write, ct);
        var correction = await fixture.BuildCorrectionAsync(ct);
        var invalid = correction with
        {
            PostingKind = LedgerPostingKindDto.Originating,
            AdjustmentApproval = null,
            PostingCommand = correction.PostingCommand! with { Intent = AccountingPostingIntentDto.Originating }
        };
        var append = () => fixture.Store.AppendAsync(invalid, ct);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*adjustment posting semantics*");
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Should().ContainSingle();

        var noApproval = () => fixture.Store.AppendAsync(correction with { AdjustmentApproval = null }, ct);
        await noApproval.Should().ThrowAsync<LedgerValidationException>().WithMessage("*adjustment posting semantics*");
        await fixture.Store.AppendAsync(correction, ct);
        var posted = await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId, ct);
        posted.Should().HaveCount(2);
        var retained = posted.Single(record => record.Entry.JournalEntryId == correction.Entry.JournalEntryId);
        retained.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        retained.SourceJournalEntryId.Should().Be(fixture.Write.Entry.JournalEntryId);
        retained.AdjustmentApproval!.ApprovalId.Should().Be(correction.PostingCommand!.ApprovalId);
        (await fixture.Service.CalculateAsync(fixture.Evidence.Request, ct)).ProposedLines.Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task DedicatedBook_RejectsOrdinaryWritesWithoutTagsRegardlessOfCallerPolicy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await AuthorityFixture.CreateAsync(ct);
        var key = $"manual-je:{fixture.Write.Entry.JournalEntryId:N}";
        var ordinary = fixture.Write with
        {
            Entry = ConsolidationStorageFixture.WithMetadata(fixture.Write.Entry,
                fixture.Write.Entry.Metadata with { Tags = null, IdempotencyKey = key, EffectiveDate = fixture.Evidence.Request.AsOf }),
            PostingCommand = fixture.Write.PostingCommand! with { IdempotencyKey = key }
        };
        var attempts = new[]
        {
            ordinary,
            ordinary with
            {
                AccountingPolicyId = "legacy-v1", AccountingPolicyVersion = "legacy-v1",
                PostingCommand = ordinary.PostingCommand! with { BookContext = null }
            },
            ordinary with
            {
                AccountingPolicyId = "legacy-v1", AccountingPolicyVersion = "legacy-v1",
                PostingCommand = null, LedgerBookId = null
            }
        };
        foreach (var write in attempts)
        {
            var append = () => fixture.Store.AppendAsync(write, ct);
            await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Dedicated consolidation books require reviewed consolidation evidence*");

            await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var externalAppend = () => fixture.Store.AppendAsync(connection, transaction, write, ct);
            await externalAppend.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Dedicated consolidation books require reviewed consolidation evidence*");
            await transaction.RollbackAsync(ct);
        }
        await fixture.AssertNoEliminationAsync(ct);

        var normalBook = ConsolidationStorageFixture.Book("Normal operating book");
        await fixture.Database.JournalStore.SaveLedgerBookAsync(normalBook, ct);
        var normalPeriod = await fixture.Database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(normalBook), 0, ct: ct);
        await fixture.Store.AppendAsync(ConsolidationStorageFixture.Write(normalBook, normalPeriod), ct);
        (await fixture.Database.JournalStore.GetByPeriodAsync(normalPeriod.PeriodId, ct)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task DirectAppendWithoutAuthorityProvider_FailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var fixture = await AuthorityFixture.CreateAsync(timeout.Token);
        var append = () => fixture.Database.JournalStore.AppendAsync(fixture.Write, timeout.Token);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*validation is unavailable*");
        await fixture.AssertNoEliminationAsync(timeout.Token);
    }

    [LedgerDatabaseFact]
    public async Task CallerOwnedTransaction_CannotReleaseAuthorityBeforeCommit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var fixture = await AuthorityFixture.CreateAsync(timeout.Token);
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, timeout.Token);
        var append = () => fixture.Store.AppendAsync(connection, transaction, fixture.Write, timeout.Token);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*standalone append transaction*");
        await transaction.RollbackAsync(timeout.Token);
        await fixture.AssertNoEliminationAsync(timeout.Token);
    }

    [LedgerDatabaseFact]
    public async Task OwnershipChangedAfterWorkflowValidation_IsRefusedAtDirectAppend()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await AuthorityFixture.CreateAsync(ct);
        await fixture.Service.ValidateEvidenceCurrentAsync(fixture.Evidence, ct);
        await fixture.Ownership.UpsertOwnershipLinkAsync(fixture.MemberLink with { OwnershipPercent = 90m }, ct);

        var append = () => fixture.Store.AppendAsync(fixture.Write, ct);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*100% ownership*");
        await fixture.AssertNoEliminationAsync(ct);
        // Failure disposes both authority leases; later ownership and policy writes can complete.
        await fixture.Ownership.UpsertOwnershipLinkAsync(fixture.MemberLink, ct);
        await fixture.ReplacePolicyAsync(ct);
    }

    [LedgerDatabaseFact]
    public async Task PolicyChangedAfterWorkflowValidation_IsRefusedAtDirectAppend()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await AuthorityFixture.CreateAsync(ct);
        await fixture.Service.ValidateEvidenceCurrentAsync(fixture.Evidence, ct);
        await fixture.ReplacePolicyAsync(ct);

        var append = () => fixture.Store.AppendAsync(fixture.Write, ct);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*renewed review*");
        await fixture.AssertNoEliminationAsync(ct);
    }

    [LedgerDatabaseFact]
    public async Task AuthorityLease_BlocksConcurrentOwnershipAndPolicyChangesUntilJournalCommit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var fixture = await AuthorityFixture.CreateAsync(ct);
        var held = new HoldingAuthority(fixture.Authority, async () =>
            (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Count == 1);
        var store = new PostgresLedgerJournalStore(fixture.Database.Options, consolidationAuthority: () => held);
        var append = store.AppendAsync(fixture.Write, ct);
        await held.Acquired.Task.WaitAsync(ct);
        var ownershipChange = fixture.Ownership.UpsertOwnershipLinkAsync(fixture.MemberLink with { OwnershipPercent = 90m }, ct);
        var policyChange = fixture.ReplacePolicyAsync(ct);
        await WaitForOwnershipWriterAsync(fixture, ct);
        policyChange.IsCompleted.Should().BeFalse("the validated policy lease excludes replacement through commit");
        ownershipChange.IsCompleted.Should().BeFalse("a competing write cannot change the effective ownership perimeter");
        held.Continue.TrySetResult();
        await append;
        await Task.WhenAll(ownershipChange, policyChange);
        held.JournalWasCommittedWhenLeaseReleased.Should().BeTrue();
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Should().ContainSingle();
    }

    private static async Task WaitForOwnershipWriterAsync(AuthorityFixture fixture, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "select exists(select 1 from pg_stat_activity where application_name = @name and cardinality(pg_blocking_pids(pid)) > 0);";
        command.Parameters.AddWithValue("name", fixture.OwnershipApplicationName);
        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
            await Task.Delay(10, ct);
    }

    private sealed class HoldingAuthority(IConsolidationPostingAuthority inner, Func<Task<bool>> isCommitted) : IConsolidationPostingAuthority
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool JournalWasCommittedWhenLeaseReleased { get; private set; }

        public async Task<IAsyncDisposable> AcquireValidatedLeaseAsync(ConsolidationEvidenceDto evidence, CancellationToken ct = default)
        {
            var lease = await inner.AcquireValidatedLeaseAsync(evidence, ct);
            try
            {
                Acquired.TrySetResult();
                await Continue.Task.WaitAsync(ct);
                return new CallbackLease(lease, async () => { JournalWasCommittedWhenLeaseReleased = await isCommitted(); });
            }
            catch { await lease.DisposeAsync(); throw; }
        }
    }

    private sealed class CallbackLease(IAsyncDisposable inner, Func<Task> beforeRelease) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            { await beforeRelease(); }
            finally { await inner.DisposeAsync(); }
        }
    }

    private sealed class AuthorityFixture : IAsyncDisposable
    {
        public required LedgerPostgresTestDatabase Database { get; init; }
        public required PostgresFundStructureStore Ownership { get; init; }
        public required OwnershipLinkDto MemberLink { get; init; }
        public required string OwnershipApplicationName { get; init; }
        public required string OwnershipSchema { get; init; }
        public required AccountingPolicyService Policies { get; init; }
        public required AccountingPolicyDto ReviewedPolicy { get; init; }
        public required ConsolidationService Service { get; init; }
        public required PostgresConsolidationPostingAuthority Authority { get; init; }
        public required PostgresLedgerJournalStore Store { get; init; }
        public required LedgerAccountingPeriod Period { get; init; }
        public required ConsolidationEvidenceDto Evidence { get; init; }
        public required LedgerJournalEntryWrite Write { get; init; }

        public static async Task<AuthorityFixture> CreateAsync(CancellationToken ct)
        {
            var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
            try
            {
                // A deliberately separate schema exercises the configured ownership authority,
                // rather than assuming ownership tables live beside the journal tables.
                var name = "authority-" + Guid.NewGuid().ToString("N");
                var options = new FundStructureStoreOptions
                {
                    ConnectionString = new NpgsqlConnectionStringBuilder(database.Options.ConnectionString) { ApplicationName = name }.ConnectionString,
                    Schema = "authority_" + Guid.NewGuid().ToString("N")
                };
                await new FundStructureMigrationRunner(options).EnsureMigratedAsync(ct);
                var ownership = new PostgresFundStructureStore(options);
                var graph = CoreFixture.Graph();
                foreach (var item in graph.Organizations)
                    await ownership.UpsertOrganizationAsync(item, ct);
                foreach (var item in graph.Businesses)
                    await ownership.UpsertBusinessAsync(item, ct);
                foreach (var item in graph.Funds)
                    await ownership.UpsertFundAsync(item, ct);
                foreach (var item in graph.Entities)
                    await ownership.UpsertLegalEntityAsync(item, ct);
                foreach (var item in graph.OwnershipLinks)
                    await ownership.UpsertOwnershipLinkAsync(item, ct);
                var accounts = new Mock<IFundAccountService>();
                accounts.Setup(service => service.QueryAccountsAsync(It.IsAny<AccountStructureQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<AccountSummaryDto>());
                var structure = new PostgresFundStructureService(ownership, accounts.Object, new FundStructurePolicyService());
                var books = new[]
                {
                    Book(CoreFixture.FirstBookId, CoreFixture.FirstId, FundStructureNodeKindDto.Entity),
                    Book(CoreFixture.SecondBookId, CoreFixture.SecondId, FundStructureNodeKindDto.Entity),
                    Book(CoreFixture.OverlayBookId, CoreFixture.RootId, FundStructureNodeKindDto.Fund) with
                    { AccountingPolicyId = "consolidation-v1", AccountingPolicyVersion = "w10-v1" }
                };
                var periods = new List<LedgerAccountingPeriod>();
                foreach (var book in books)
                {
                    await database.JournalStore.SaveLedgerBookAsync(book, ct);
                    periods.Add(await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(book, 6), 0, ct: ct));
                }
                await AppendSourceAsync(database, books[0], periods[0], CoreFixture.FirstId, CoreFixture.SecondId, true, ct);
                await AppendSourceAsync(database, books[1], periods[1], CoreFixture.SecondId, CoreFixture.FirstId, false, ct);
                var policies = new AccountingPolicyService();
                var service = new ConsolidationService(new ConsolidationPerimeterResolver(structure), database.JournalStore,
                    policies, new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)));
                var calculation = await service.CalculateAsync(new(CoreFixture.OrganizationId, CoreFixture.RootId,
                    CoreFixture.OverlayBookId, periods[2].PeriodId, CoreFixture.Date), ct);
                var authority = new PostgresConsolidationPostingAuthority(service, ownership, policies);
                var write = CreateReviewedWrite(calculation);
                return new AuthorityFixture
                {
                    Database = database,
                    Ownership = ownership,
                    MemberLink = graph.OwnershipLinks.Single(link => link.ChildNodeId == CoreFixture.FirstId),
                    OwnershipApplicationName = name,
                    OwnershipSchema = options.Schema,
                    Policies = policies,
                    ReviewedPolicy = calculation.Policy,
                    Service = service,
                    Authority = authority,
                    Store = new(database.Options, consolidationAuthority: () => authority),
                    Period = periods[2],
                    Evidence = calculation.Evidence,
                    Write = write
                };
            }
            catch { await database.DisposeAsync(); throw; }
        }

        public async Task ReplacePolicyAsync(CancellationToken ct)
        {
            var policy = ReviewedPolicy;
            await Policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(policy.AccountingBasis, policy.PolicyId,
                policy.Version, "Changed after review", policy.EffectiveFrom, policy.EffectiveTo, policy.IsDefault,
                policy.RulesJson, FundProfileId: policy.FundProfileId, FundStructureNodeId: policy.FundStructureNodeId,
                RulePack: policy.RulePack), ct);
        }

        public async Task<LedgerJournalEntryWrite> BuildCorrectionAsync(CancellationToken ct)
        {
            var book = (await Database.JournalStore.GetLedgerBookAsync(CoreFixture.SecondBookId, ct))!;
            var source = await Database.JournalStore.QueryAsync(new(LedgerBookId: book.LedgerBookId), ct);
            var period = (await Database.JournalStore.GetPeriodAsync(source[0].PeriodId, ct))!;
            await AppendSourceAsync(Database, book, period, CoreFixture.SecondId, CoreFixture.FirstId, false, ct);
            var calculation = await Service.CalculateAsync(Evidence.Request, ct);
            return ConsolidationStorageFixture.AsReviewedCorrection(CreateReviewedWrite(calculation), Write.Entry.JournalEntryId);
        }

        private static LedgerJournalEntryWrite CreateReviewedWrite(ConsolidationCalculation calculation)
        {
            var write = ConsolidationStorageFixture.Write(calculation.Book, calculation.Period, calculation.Request.AsOf) with
            { AccountingPolicyId = calculation.Book.AccountingPolicyId, AccountingPolicyVersion = calculation.Book.AccountingPolicyVersion };
            var lines = calculation.Evidence.ExpectedLines.Select(line => new LedgerEntry(Guid.NewGuid(), write.Entry.JournalEntryId,
                write.Entry.Timestamp, new(line.AccountPath, line.AccountPath == ConsolidationService.ReceivableAccount ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, write.Entry.Description,
                new(FundId: line.Dimensions!.FundId, EntityId: line.EntityId, CounterpartyId: line.Dimensions.CounterpartyId))).ToArray();
            write = write with { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description, lines) };
            return ConsolidationStorageFixture.WithEvidence(write, calculation.Evidence);
        }

        public async Task AssertNoEliminationAsync(CancellationToken ct) =>
            (await Database.JournalStore.GetByPeriodAsync(Period.PeriodId, ct)).Should().BeEmpty();

        private static LedgerBookRecord Book(Guid id, Guid entity, FundStructureNodeKindDto kind) =>
            new(id, CoreFixture.Profile, entity, kind, entity.ToString("D"), "USD", CoreFixture.Time, CoreFixture.Time);

        private static async Task AppendSourceAsync(LedgerPostgresTestDatabase database, LedgerBookRecord book,
            LedgerAccountingPeriod period, Guid entity, Guid counterparty, bool receivable, CancellationToken ct)
        {
            var write = ConsolidationStorageFixture.Write(book, period, CoreFixture.Date);
            var account = new LedgerAccount(receivable ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount,
                receivable ? LedgerAccountType.Asset : LedgerAccountType.Liability);
            var amount = receivable ? 100m : 80m;
            var lines = new LedgerEntry[]
            {
                new(Guid.NewGuid(), write.Entry.JournalEntryId, write.Entry.Timestamp, account, receivable ? amount : 0m, receivable ? 0m : amount,
                    write.Entry.Description, new(EntityId: entity.ToString("D"), CounterpartyId: counterparty.ToString("D"))),
                new(Guid.NewGuid(), write.Entry.JournalEntryId, write.Entry.Timestamp,
                    new(receivable ? "Equity:Opening" : "Assets:Cash", receivable ? LedgerAccountType.Equity : LedgerAccountType.Asset),
                    receivable ? 0m : amount, receivable ? amount : 0m, write.Entry.Description, new(EntityId: entity.ToString("D")))
            };
            await database.JournalStore.AppendAsync(write with
            { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description, lines) }, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var connection = new NpgsqlConnection(Database.Options.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"drop schema if exists \"{OwnershipSchema}\" cascade;";
                await command.ExecuteNonQueryAsync();
            }
            finally { await Database.DisposeAsync(); }
        }
    }
}
