using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Tests.Storage.FundAccounts;
using Meridian.TestSupport;

namespace Meridian.Tests.Integration;

/// <summary>Exercises the composed feature, rather than constructing approved registry snapshots by hand.</summary>
[Trait("Category", "Integration")]
public sealed partial class IntercompanyAgreementFeaturePostgresTests
{
    [FundAccountDatabaseFact]
    public async Task ApprovedRegistryRevisionsGovernRealLedgerAppendsWithoutRewritingPostedProvenance()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = timeout.Token;
        await using var data = await Fixture.CreateAsync(ct);
        await data.ApproveAsync(data.Draft, ct);
        var approved = await data.Service.GetAsync(data.Author, data.AgreementId, new(2026, 5, 15), ct: ct);
        Assert.Equal(1, approved!.ApplicableRevision!.AgreementVersion);
        var originalRevision = JsonSerializer.Serialize(approved.Revisions[0]);

        // The authority database and ledger use independent configured schemas/connections.
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = data.Options.ConnectionString,
            SchemaName = data.Server.CreateSchemaName("ic_feature_ledger")
        };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        var authority = new PostgresIntercompanyAgreementPostingAuthority(data.Store);
        var ledger = new PostgresLedgerJournalStore(ledgerOptions, intercompanyAgreementAuthority: () => authority);
        var book = IntercompanyAgreementPostingGuardTests.Book();
        await new PostgresFundProfileTenancyRegistry(ledgerOptions).BindAsync(book.FundProfileId, Fixture.Tenant, ct: ct);
        await ledger.SaveLedgerBookAsync(book, ct);
        var period = await ledger.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5,
            "Synthetic agreement feature period", new(2026, 5, 1), new(2026, 5, 31), "Open",
            DateTimeOffset.UtcNow, null, 0), 0, ct: ct);

        LedgerJournalEntryWrite NewWrite(int version) => IntercompanyAgreementPostingGuardTests.Write(
            IntercompanyAgreementPostingGuardTests.Dimensions() with
            { IntercompanyAgreementId = data.AgreementId, IntercompanyAgreementVersion = version })
            with
        { LedgerBookId = book.LedgerBookId, PeriodId = period.PeriodId };
        var source = NewWrite(1);
        await ledger.AppendAsync(source, ct);

        await data.ApproveAsync(data.Draft with
        {
            Action = IntercompanyAgreementActionDto.Amend,
            EffectiveFrom = new(2026, 2, 1),
            Terms = new(FixedAnnualRate: 0.075m, Notes: "Reviewed retrospective synthetic amendment")
        }, ct);
        var amended = await data.Service.GetAsync(data.Author, data.AgreementId, new(2026, 5, 15), ct: ct);
        Assert.Equal(2, amended!.ApplicableRevision!.AgreementVersion);
        Assert.Equal(originalRevision, JsonSerializer.Serialize(amended.Revisions[0]));
        Assert.Equal(6, amended.AggregateVersion);

        await Assert.ThrowsAsync<LedgerValidationException>(() => ledger.AppendAsync(NewWrite(1), ct));
        await ledger.AppendAsync(NewWrite(2), ct);
        // An exact already-committed source retry recovers despite the later agreement revision.
        await ledger.AppendAsync(source, ct);
        var retained = Assert.Single(await ledger.QueryAsync(new(JournalEntryId: source.Entry.JournalEntryId), ct));
        Assert.Equal(1, retained.Entry.Lines[0].Dimensions!.IntercompanyAgreementVersion);
        Assert.Equal(data.AgreementId, retained.Entry.Lines[0].Dimensions!.IntercompanyAgreementId);

        await data.ApproveAsync(data.Draft with
        {
            Action = IntercompanyAgreementActionDto.Terminate,
            TerminationEffectiveDate = new(2026, 5, 20)
        }, ct);
        var terminated = await data.Service.GetAsync(data.Author, data.AgreementId, new(2026, 5, 31), ct: ct);
        Assert.Equal(IntercompanyAgreementStatusDto.Terminated, terminated!.EffectiveStatus);
        var afterCutoff = NewWrite(2);
        afterCutoff = afterCutoff with
        {
            Entry = new(afterCutoff.Entry.JournalEntryId, afterCutoff.Entry.Timestamp, afterCutoff.Entry.Description,
                afterCutoff.Entry.Lines, afterCutoff.Entry.Metadata with { EffectiveDate = new(2026, 5, 31) })
        };
        await Assert.ThrowsAsync<LedgerValidationException>(() => ledger.AppendAsync(afterCutoff, ct));
        // Only the durable boundary's verified exact source inverse may retain historical v1.
        var reversal = IntercompanyAgreementPostingGuardTests.Reverse(source);
        await ledger.AppendAsync(reversal, ct);
        var postedReversal = Assert.Single(await ledger.QueryAsync(new(JournalEntryId: reversal.Entry.JournalEntryId), ct));
        Assert.Equal(1, postedReversal.Entry.Lines[0].Dimensions!.IntercompanyAgreementVersion);
        Assert.Equal(source.Entry.Lines[0].Debit, postedReversal.Entry.Lines[0].Credit);
        Assert.Equal(3, (await ledger.GetByPeriodAsync(period.PeriodId, ct)).Count);
        var persisted = await new PostgresIntercompanyAgreementStore(data.Options).GetAsync(Fixture.Tenant, data.AgreementId, ct);
        Assert.Equal(9, persisted!.AggregateVersion);
        Assert.Equal(3, persisted.Revisions.Count);
        Assert.Equal(originalRevision, JsonSerializer.Serialize(persisted.Revisions[0]));
    }

    [FundAccountDatabaseFact]
    public async Task ApprovalEvidenceRecheckAndDistinctReviewerAreEnforcedInsideThePostgresCommand()
    {
        await using var data = await Fixture.CreateAsync(CancellationToken.None);
        var create = await data.CommandAsync(data.Draft);
        await data.Service.CreateAsync(data.Author, create);
        var submitted = await data.Service.SubmitAsync(data.Author, await data.CommandAsync(proposalId: create.ProposalId));
        var approval = await data.CommandAsync(proposalId: create.ProposalId);
        approval = approval with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash };
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => data.Service.ApproveAsync(data.Author, approval));
        data.Evidence.Hash = "changed-synthetic-document-hash";
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => data.Service.ApproveAsync(data.Reviewer, approval));
        var retained = await new PostgresIntercompanyAgreementStore(data.Options).GetAsync(Fixture.Tenant, data.AgreementId);
        Assert.Equal(2, retained!.AggregateVersion);
        Assert.Empty(retained.Revisions);
        Assert.Equal(2, retained.Receipts.Count);
        Assert.Equal(IntercompanyAgreementProposalStateDto.Submitted, retained.Proposals[^1].State);
    }

    private sealed class Fixture(PostgresTestServer server, FundStructureStoreOptions options) : IAsyncDisposable
    {
        public const string Tenant = "tenant-agreement";
        public PostgresTestServer Server => server;
        public FundStructureStoreOptions Options => options;
        public Guid AgreementId { get; } = Guid.NewGuid();
        public PostgresIntercompanyAgreementStore Store { get; } = new(options);
        public IntercompanyAgreementCallContext Author { get; } = new(Tenant, "synthetic-author");
        public IntercompanyAgreementCallContext Reviewer { get; } = new(Tenant, "synthetic-independent-reviewer");
        public SyntheticEvidenceAuthority Evidence { get; } = new();
        public IntercompanyAgreementService Service => new(Store, new SyntheticAccessAuthority(), Evidence, new Clock());
        public IntercompanyAgreementDraftDto Draft { get; } = new("IC-SYNTHETIC", "Synthetic feature agreement",
            Guid.Parse(IntercompanyAgreementPostingGuardTests.Dimensions().EntityId!), "Lender",
            Guid.Parse(IntercompanyAgreementPostingGuardTests.Dimensions().CounterpartyId!), "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), Terms: new(FixedAnnualRate: 0.05m),
            Evidence: [new("synthetic-retained-vault:agreement-document")]);

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING", ct: ct);
            var options = new FundStructureStoreOptions
            { ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("ic_feature") };
            try
            {
                await new FundStructureMigrationRunner(options).EnsureMigratedAsync(ct);
                var fixture = new Fixture(server, options);
                var structure = new PostgresFundStructureStore(options);
                foreach (var entity in new[] { fixture.Draft.PartyOneId, fixture.Draft.PartyTwoId })
                {
                    await structure.UpsertLegalEntityAsync(new(entity, LegalEntityTypeDto.Vehicle, entity.ToString("N"),
                        "Synthetic feature entity", "US", "USD", true, new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null), ct);
                    await structure.StampNodeTenantAsync(entity, Tenant, ct);
                }
                return fixture;
            }
            catch { await server.DisposeAsync(); throw; }
        }

        public async Task<IntercompanyAgreementCommandDto> CommandAsync(IntercompanyAgreementDraftDto? draft = null, Guid? proposalId = null)
        {
            var version = (await Store.GetAsync(Tenant, AgreementId))?.AggregateVersion ?? 0;
            return new(Guid.NewGuid(), AgreementId, version, proposalId ?? Guid.NewGuid(), draft,
                "Reviewed synthetic retrospective agreement rationale");
        }

        public async Task ApproveAsync(IntercompanyAgreementDraftDto draft, CancellationToken ct)
        {
            var create = await CommandAsync(draft);
            if (create.ExpectedAggregateVersion == 0)
                await Service.CreateAsync(Author, create, ct);
            else
                await Service.CreateProposalAsync(Author, create, ct);
            var submitted = await Service.SubmitAsync(Author, await CommandAsync(proposalId: create.ProposalId), ct);
            var approve = await CommandAsync(proposalId: create.ProposalId);
            await Service.ApproveAsync(Reviewer, approve with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash }, ct);
        }
        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class SyntheticAccessAuthority : IIntercompanyAgreementAccessAuthority
    {
        public Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> entities, CancellationToken ct = default)
            => Task.FromResult(context.TenantId == Fixture.Tenant && entities.Count == 2);
    }
    private sealed class SyntheticEvidenceAuthority : IIntercompanyAgreementEvidenceAuthority
    {
        public string Hash { get; set; } = "synthetic-retained-document-hash";
        public Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(IntercompanyAgreementCallContext context,
            Guid agreementId, Guid proposalId, IReadOnlyList<IntercompanyAgreementEvidenceDto> evidence, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(evidence.Select(item =>
                new IntercompanyAgreementEvidenceDto(item.Reference, "synthetic-document", Hash)).ToArray());
    }
}
