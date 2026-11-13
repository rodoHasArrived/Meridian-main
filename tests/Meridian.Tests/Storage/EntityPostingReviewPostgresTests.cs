using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Storage;

public sealed class EntityPostingReviewPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task WindingDownReview_RetainsRealAuthorityWithoutPosting_AppendNeedsExactControlledClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var options = new FundStructureStoreOptions { ConnectionString = database.Options.ConnectionString,
            Schema = database.Options.SchemaName + "_review_structure" };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(ct);
            var graphStore = new PostgresFundStructureStore(options);
            var book = ConsolidationStorageFixture.Book("Controlled settlement") with { FundStructureNodeKind = FundStructureNodeKindDto.Entity };
            var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var revision = EntityLifecycleRevisionIntegrity.Seal(new("tenant-a", book.FundStructureNodeId, 1, start,
                "reviewer", "Wind down", "document://lifecycle", "", "",
                [new(Guid.NewGuid(), start, null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Formation)]));
            var history = new InMemoryEntityLifecycleStore();
            await history.AppendAsync(revision, 0, ct);
            var graph = new OrganizationStructureGraphDto([], [], [], [], [], [],
                [new(book.FundStructureNodeId, LegalEntityTypeDto.Fund, "E", "Closing entity", "US", "USD", true, start, null)], [], [],
                [new(book.FundStructureNodeId, FundStructureNodeKindDto.Entity, "E", "Closing entity", null, true, start, null)], [], []);
            var structures = new Mock<IFundStructureService>();
            structures.Setup(item => item.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(graph);
            PostgresLedgerJournalStore? store = null;
            var lifecycle = new LifecycleLedgerPostingAuthority(structures.Object, history, () => store,
                graphStore.AcquireConsolidationAuthorityLeaseAsync);
            store = new PostgresLedgerJournalStore(database.Options, new ConsolidationTransitionStoreTests.TenantAccessor("tenant-a"),
                postingAuthorities: () => new ILedgerPostingAuthority[] { lifecycle }
                    .Concat(LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities().Where(item => item.ControlName != "entity-lifecycle")));
            await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(book.FundProfileId, "tenant-a", ct: ct);
            await store.SaveLedgerBookAsync(book, ct);
            var period = await store.SavePeriodAsync(ConsolidationStorageFixture.Period(book), 0, ct: ct);
            var write = ConsolidationStorageFixture.Write(book, period);
            var review = write with { PostingCommand = write.PostingCommand! with
                { ApprovalState = AccountingPostingApprovalStateDto.NotRequired, ApprovalId = null } };

            await store.ValidateEntityPostingControlsAsync(review, ct);
            Assert.Empty(await store.GetByPeriodAsync(period.PeriodId, ct));
            await Assert.ThrowsAsync<LedgerValidationException>(() => store.AppendAsync(write, ct));
            Assert.Empty(await store.GetByPeriodAsync(period.PeriodId, ct));

            // Review defers lifecycle operation eligibility, not native legal provenance.
            var line = review.Entry.Lines[0];
            var canonical = new LedgerLineDimensionSet(EntityId: book.FundStructureNodeId.ToString("D"), CounterpartyId: Guid.NewGuid().ToString("D"))
                { IntercompanyAgreementId = Guid.NewGuid(), IntercompanyAgreementVersion = 1 };
            var unverifiedLegal = review with { Entry = new(review.Entry.JournalEntryId, review.Entry.Timestamp, review.Entry.Description,
                [new(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account, line.Debit, line.Credit, line.Description, canonical), review.Entry.Lines[1]], review.Entry.Metadata) };
            var missingLegal = await Assert.ThrowsAsync<LedgerValidationException>(() => store.ValidateEntityPostingControlsAsync(unverifiedLegal, ct));
            Assert.Contains("intercompany agreement validation is unavailable", missingLegal.Message);
            Assert.Empty(await store.GetByPeriodAsync(period.PeriodId, ct));

            var authorization = new ControlledCloseAuthorizationDto(Guid.NewGuid(), write.Entry.JournalEntryId, book.LedgerBookId,
                book.FundProfileId, 4, "tenant-a", EntityLifecycleOperationDto.Settlement, ControlledCloseFingerprint.Compute(write),
                write.PostingCommand!.ApprovalId!, "document://existing-obligation", "Reviewed final settlement", "reviewer", DateTimeOffset.UtcNow,
                EntityLifecycleRevisions: new Dictionary<string, string> { [book.FundStructureNodeId.ToString("D")] = revision.ContentHash });
            await store.SaveCloseAuthorizationAsync(authorization, ct);
            var authorized = write with { PostingCommand = write.PostingCommand with { ControlledCloseAuthorizationId = authorization.AuthorizationId } };
            var changedLines = write.Entry.Lines.Select(item => new LedgerEntry(item.EntryId, item.JournalEntryId, item.Timestamp,
                item.Account, item.Debit > 0m ? item.Debit + 20m : 0m, item.Credit > 0m ? item.Credit + 20m : 0m,
                item.Description, item.Dimensions, item.Currency)).ToArray();
            var changed = authorized with { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
                changedLines, write.Entry.Metadata) };
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(changed, ct));
            Assert.Empty(await store.GetByPeriodAsync(period.PeriodId, ct));
            await store.AppendAsync(authorized, ct);
            Assert.Single(await store.GetByPeriodAsync(period.PeriodId, ct));
        }
        finally
        {
            await using var cleanup = new Npgsql.NpgsqlConnection(options.ConnectionString);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var command = new Npgsql.NpgsqlCommand($"drop schema if exists {options.Schema} cascade", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
