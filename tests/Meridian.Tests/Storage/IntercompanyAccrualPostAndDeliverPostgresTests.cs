using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Storage;

/// <summary>
/// Month-end interest on an intercompany loan, end to end over durable stores: the lender's book holds a
/// posted drawdown, the governed accrual is drafted from approved terms, reviewed and posted, the retained
/// proposal is delivered into the borrower's review queue, and the borrower posts its own side.
/// Every store is durable: the ledger, proposal outbox, account bindings, chart and book ownership in the ledger
/// schema; the native legal agreement registry, accounting profile history, legal-entity graph and entity
/// lifecycle history in a fund-structure schema of the same PostgreSQL database; and journal drafts in the file
/// store the workstation host registers. The intercompany, legal-agreement and entity-lifecycle posting
/// authorities are the production classes. Only the agreement access scope, the evidence vault, the profile
/// scope authority and the fund-account read service are synthetic.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IntercompanyAccrualPostAndDeliverPostgresTests
{
    private const string Tenant = "tenant-a";
    private const string Company = "company-a";
    private const string LenderFund = "accrual-lender-fund";
    private const string BorrowerFund = "accrual-borrower-fund";
    private const string Transaction = "loan-interest:20260601-20260701";
    private static readonly Guid Organization = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid Lender = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid Borrower = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid LenderBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid BorrowerBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid PrincipalProfile = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid InterestProfile = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Formed = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProfileStart = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reviewed = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [LedgerDatabaseFact]
    public async Task ReviewedAccrual_PostsAtSource_DeliversOneCounterpartDraft_AndPostsAtDestinationWithoutMirroringBack()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await using var fixture = await SetupAsync(database);
        using var target = fixture.Target;

        // The lender advanced 1,000,000 on 15 May. The drawdown's own counterpart is delivered to the borrower
        // and left in review: principal is read from the lender's posted journals only.
        await fixture.Journals.AppendAsync(fixture.Drawdown(1_000_000m));
        Assert.Equal(1, await fixture.Delivery.RunOnceAsync("worker"));

        // 1,000,000 x 5% x 30/360 = 4,166.6666... -> 4,166.67, computed from approved terms and posted principal.
        var request = new IntercompanyAccrualRequestDto(LenderBook, InterestProfile, PrincipalProfile, From, To);
        var preview = await fixture.Accrual.PreviewAsync(Tenant, Company, "accrual-preparer", request);
        Assert.True(preview.IsReady, string.Join(" | ", preview.Diagnostics));
        Assert.Equal(4166.67m, preview.Computation.Amount);
        Assert.Equal(1_000_000m, preview.Evidence!.Principal.OpeningPrincipal);
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.LenderJune.PeriodId));

        var draft = await fixture.Accrual.CreateDraftAsync(Tenant, Company, "accrual-preparer", new(request, preview.EvidenceDigest!));
        Assert.Equal(ManualJournalEntryStatusDto.Draft, draft.Status);
        Assert.Empty(draft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(issue => $"{issue.Code}: {issue.Message}"));
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.LenderJune.PeriodId));

        var sourcePosted = await fixture.ReviewAndPostAsync(draft, LenderFund, LenderBook, fixture.LenderJune, "accrual-preparer", "lender-reviewer");

        var sourceJournal = Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.LenderJune.PeriodId));
        Assert.Equal(sourcePosted.JournalEntryId, sourceJournal.Entry.JournalEntryId);
        Assert.Equal(new DateOnly(2026, 6, 30), sourceJournal.Entry.Metadata.EffectiveDate);
        Assert.Equal(preview.EvidenceDigest, sourceJournal.Entry.Metadata.Tags![IntercompanyAccrualEvidenceGuard.DigestTag]);
        Assert.Equal(preview.EvidenceDigest, IntercompanyAccrualEvidenceGuard.Digest(sourceJournal.Entry.Metadata.Tags[IntercompanyAccrualEvidenceGuard.EvidenceTag]));
        var receivable = Assert.Single(sourceJournal.Entry.Lines, line => line.Dimensions?.ChartAccountNodeId == "a-int-recv");
        Assert.Equal((4166.67m, 0m), (receivable.Debit, receivable.Credit));
        Assert.Equal(Transaction, receivable.Dimensions!.IntercompanyTransactionId);
        // The posted line pins the exact revision the native registry approved, not a fixture-built snapshot.
        Assert.Equal((fixture.LegalRevision.AgreementId, fixture.LegalRevision.AgreementVersion),
            (receivable.Dimensions.IntercompanyAgreementId, receivable.Dimensions.IntercompanyAgreementVersion));
        Assert.Equal(ManualJournalEntryStatusDto.Posted,
            (await fixture.ReopenDrafts().GetAsync(LenderFund, sourcePosted.JournalEntryId, default, Tenant, Company))!.Status);

        // Posting the source committed its proposal with the journal; the borrower's book is still untouched.
        var pending = Assert.Single(await fixture.Proposals.ListAsync(Tenant, LenderBook), item => item.Source.TransactionId == Transaction);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, pending.Status);
        Assert.Equal(sourcePosted.JournalEntryId, pending.Source.SourceJournalId);
        Assert.Equal(InterestProfile, pending.Source.AccountingArrangementId);
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.BorrowerJune.PeriodId));

        Assert.Equal(1, await fixture.Delivery.RunOnceAsync("worker"));

        var delivered = (await fixture.Proposals.GetAsync(Tenant, pending.Source.ProposalId))!;
        Assert.Equal(IntercompanyProposalStatusDto.Drafted, delivered.Status);
        Assert.Null(delivered.LastError);
        Assert.Equal(BorrowerBook, delivered.TargetBookId);
        var counterpart = (await fixture.Drafts.GetAsync(BorrowerFund, delivered.TargetJournalId!.Value, default, Tenant, Company))!;
        Assert.Equal(ManualJournalEntryStatusDto.Draft, counterpart.Status);
        Assert.Null(counterpart.ApprovedBy);
        Assert.Equal(pending.Source.ProposalId, counterpart.IntercompanyProposalId);
        Assert.Equal(new DateOnly(2026, 6, 30), counterpart.AccountingDate);
        Assert.Equal([("b-int-expense", AccountingTemplateLineSideDto.Debit, 4166.67m), ("b-int-pay", AccountingTemplateLineSideDto.Credit, 4166.67m)],
            counterpart.Lines.Select(line => (line.Dimensions!.ChartAccountNodeId!, line.Side, line.Amount)).OrderBy(line => line.Item1, StringComparer.Ordinal));
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.BorrowerJune.PeriodId));
        // The delivered draft is retained on disk, so a restarted host finds the same reviewable counterpart.
        var reopened = (await fixture.ReopenDrafts().GetAsync(BorrowerFund, counterpart.JournalEntryId, default, Tenant, Company))!;
        Assert.Equal((counterpart.Version, counterpart.Status, counterpart.IntercompanyProposalId, counterpart.IntercompanyProposalSourceDigest),
            (reopened.Version, reopened.Status, reopened.IntercompanyProposalId, reopened.IntercompanyProposalSourceDigest));

        // The borrower reviews and posts its own side; the source preparer never touches the destination book.
        var destinationPosted = await fixture.ReviewAndPostAsync(counterpart, BorrowerFund, BorrowerBook, fixture.BorrowerJune,
            counterpart.PreparedBy, "borrower-reviewer", "borrower-preparer");

        var destinationJournal = Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.BorrowerJune.PeriodId));
        Assert.Equal(destinationPosted.JournalEntryId, destinationJournal.Entry.JournalEntryId);
        var payable = Assert.Single(destinationJournal.Entry.Lines, line => line.Dimensions?.ChartAccountNodeId == "b-int-pay");
        Assert.Equal((0m, 4166.67m), (payable.Debit, payable.Credit));
        Assert.Equal(Transaction, payable.Dimensions!.IntercompanyTransactionId);
        Assert.Equal(InterestProfile.ToString("D"), payable.Dimensions.AccountingArrangementId);
        Assert.Equal(receivable.Dimensions.IntercompanyAgreementId, payable.Dimensions.IntercompanyAgreementId);
        Assert.Equal(receivable.Dimensions.IntercompanyAgreementVersion, payable.Dimensions.IntercompanyAgreementVersion);
        Assert.Equal(pending.Source.ProposalId.ToString("D"), destinationJournal.Entry.Metadata.Tags![IntercompanyAccrualEvidenceGuard.CounterpartOriginTag]);

        // The generated counterpart is not itself a new source, and the same days cannot be accrued again.
        Assert.Empty(await fixture.Proposals.ListAsync(Tenant, BorrowerBook));
        Assert.Equal(0, await fixture.Delivery.RunOnceAsync("worker"));
        var again = await fixture.Accrual.PreviewAsync(Tenant, Company, "accrual-preparer", request);
        Assert.False(again.IsReady);
        Assert.Contains(Transaction, again.Diagnostics[^1]);
    }

    [LedgerDatabaseFact]
    public async Task DeliveredCounterpart_ForABorrowerDissolvedInDurableLifecycleHistory_IsRefusedAndTheBorrowerBookStaysEmpty()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await using var fixture = await SetupAsync(database);
        using var target = fixture.Target;
        await fixture.Journals.AppendAsync(fixture.Drawdown(1_000_000m));
        Assert.Equal(1, await fixture.Delivery.RunOnceAsync("worker"));
        var request = new IntercompanyAccrualRequestDto(LenderBook, InterestProfile, PrincipalProfile, From, To);
        var preview = await fixture.Accrual.PreviewAsync(Tenant, Company, "accrual-preparer", request);
        var draft = await fixture.Accrual.CreateDraftAsync(Tenant, Company, "accrual-preparer", new(request, preview.EvidenceDigest!));
        await fixture.ReviewAndPostAsync(draft, LenderFund, LenderBook, fixture.LenderJune, "accrual-preparer", "lender-reviewer");
        Assert.Equal(1, await fixture.Delivery.RunOnceAsync("worker"));
        var pending = Assert.Single(await fixture.Proposals.ListAsync(Tenant, LenderBook), item => item.Source.TransactionId == Transaction);
        var delivered = (await fixture.Proposals.GetAsync(Tenant, pending.Source.ProposalId))!;
        var counterpart = (await fixture.Drafts.GetAsync(BorrowerFund, delivered.TargetJournalId!.Value, default, Tenant, Company))!;

        // The borrower was dissolved on 15 June, before the 30 June accrual date, in its retained lifecycle history.
        await fixture.DissolveAsync(Borrower, new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

        var refusal = await Record.ExceptionAsync(() => fixture.ReviewAndPostAsync(counterpart, BorrowerFund, BorrowerBook, fixture.BorrowerJune,
            counterpart.PreparedBy, "borrower-reviewer", "borrower-preparer"));

        // The refusal is the production entity-lifecycle control reading the retained history, not any fixture rule.
        var lifecycleRefusal = Assert.IsType<LedgerValidationException>(refusal);
        Assert.Contains("Dissolved and merged entities are read-only", lifecycleRefusal.Message);
        Assert.Contains("entity-lifecycle control", lifecycleRefusal.Message);
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.BorrowerJune.PeriodId));
        Assert.NotEqual(ManualJournalEntryStatusDto.Posted,
            (await fixture.ReopenDrafts().GetAsync(BorrowerFund, counterpart.JournalEntryId, default, Tenant, Company))!.Status);
        // The lender's posted accrual is untouched by the borrower-side refusal.
        Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.LenderJune.PeriodId));
    }

    private static async Task<Fixture> SetupAsync(LedgerPostgresTestDatabase database)
    {
        var structureOptions = new FundStructureStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.Options.SchemaName + "_accrual_structure"
        };
        var draftPath = Path.Combine(Path.GetTempPath(), "meridian-accrual-drafts-" + Guid.NewGuid().ToString("N"), "manual-journal-drafts.json");
        try
        {
            return await SetupAsync(database, structureOptions, draftPath);
        }
        catch
        {
            await DropAsync(structureOptions, draftPath);
            throw;
        }
    }

    private static async Task<Fixture> SetupAsync(LedgerPostgresTestDatabase database, FundStructureStoreOptions structureOptions, string draftPath)
    {
        var tenants = new TenantAccessor();
        await new FundStructureMigrationRunner(structureOptions).EnsureMigratedAsync();
        var graph = new PostgresFundStructureStore(structureOptions);
        var lifecycle = new PostgresEntityLifecycleStore(structureOptions);
        // The organization graph only exposes entities reachable from the structure, so both legal entities sit
        // under one fund of one business of the organization that owns the accounting profiles.
        var fund = Guid.Parse("e1111111-1111-1111-1111-111111111111");
        var business = Guid.Parse("e2222222-2222-2222-2222-222222222222");
        await graph.UpsertOrganizationAsync(new(Organization, "ORG", "Synthetic group", "USD", true, Formed, null, [business]));
        await graph.UpsertBusinessAsync(new(business, Organization, BusinessKindDto.FundManager, "MGR", "Synthetic manager", "USD", true, Formed, null,
            [], [fund], []));
        await graph.UpsertFundAsync(new(fund, business, "FUND", "Synthetic lending fund", "USD", true, Formed, null, [], [], [Lender, Borrower], [], []));
        foreach (var node in new[] { Organization, business, fund })
            await graph.StampNodeTenantAsync(node, Tenant);
        foreach (var (entity, code) in new[] { (Lender, "LENDER"), (Borrower, "BORROWER") })
        {
            await graph.UpsertLegalEntityAsync(new(entity, LegalEntityTypeDto.Vehicle, code, $"Synthetic {code.ToLowerInvariant()} entity",
                "US", "USD", true, Formed, null));
            await graph.StampNodeTenantAsync(entity, Tenant);
            await lifecycle.AppendAsync(EntityLifecycleRevisionIntegrity.Seal(new(Tenant, entity, 1, Formed, "lifecycle-reviewer",
                "Evidenced synthetic formation", "document://synthetic-formation", "", "",
                [new(Guid.NewGuid(), Formed, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation)])), 0);
        }
        var owners = new PostgresFundProfileTenancyRegistry(database.Options);
        await owners.BindAsync(LenderFund, Tenant, Company);
        await owners.BindAsync(BorrowerFund, Tenant, Company);
        var plain = new PostgresLedgerJournalStore(database.Options, tenants);
        await plain.SaveLedgerBookAsync(new(LenderBook, LenderFund, Lender, FundStructureNodeKindDto.Entity, "Lender", "USD", ProfileStart, ProfileStart));
        await plain.SaveLedgerBookAsync(new(BorrowerBook, BorrowerFund, Borrower, FundStructureNodeKindDto.Entity, "Borrower", "USD", ProfileStart, ProfileStart));

        IReadOnlyList<ChartOfAccountsNodeDto> lenderChart =
        [
            Node("a-loan-recv", "Assets", "Loan receivable from borrower", "Asset", IntercompanyAccountRoleDto.Receivable),
            Node("a-int-recv", "Assets", "Interest receivable from borrower", "Asset", IntercompanyAccountRoleDto.Receivable),
            Node("a-int-income", "Income", "Intercompany interest income", "Revenue", IntercompanyAccountRoleDto.Revenue),
            Node("a-cash", "Assets", "Cash", "Asset", null)
        ];
        IReadOnlyList<ChartOfAccountsNodeDto> borrowerChart =
        [
            Node("b-loan-pay", "Liabilities", "Loan payable to lender", "Liability", IntercompanyAccountRoleDto.Payable),
            Node("b-int-pay", "Liabilities", "Interest payable to lender", "Liability", IntercompanyAccountRoleDto.Payable),
            Node("b-int-expense", "Expenses", "Intercompany interest expense", "Expense", IntercompanyAccountRoleDto.Expense),
            Node("b-cash", "Assets", "Cash", "Asset", null)
        ];
        var lenderWorkspace = Workspace(LenderFund, LenderBook, lenderChart);
        var borrowerWorkspace = Workspace(BorrowerFund, BorrowerBook, borrowerChart);
        await database.AccountingConfigurationStore.SaveAsync(lenderWorkspace);
        await database.AccountingConfigurationStore.SaveAsync(borrowerWorkspace);
        var bindings = new PostgresIntercompanyAccountBindingStore(database.Options, tenants);
        var bound = new Dictionary<string, IntercompanyAccountBindingDto>(StringComparer.Ordinal);
        foreach (var (book, chart) in new[] { (LenderBook, lenderChart), (BorrowerBook, borrowerChart) })
            foreach (var account in chart.Where(account => account.IntercompanyRole.HasValue))
                bound[account.NodeId] = await bindings.AppendAsync(new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book,
                    account.NodeId, account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new DateOnly(2026, 1, 1),
                    null, [], Tenant, Company, "mapping-reviewer", Reviewed, "Reviewed synthetic intercompany account role"), null);

        // One legal loan agreement with structured fixed-rate terms, authored, submitted and approved by different
        // people in the native PostgreSQL registry. Both accounting profiles pin its exact approved revision.
        var terms = new IntercompanyAgreementTermsDto
        {
            StructuredTerms = new(new(new(2026, 5, 1), new(2027, 5, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple)
        };
        var agreements = new PostgresIntercompanyAgreementStore(structureOptions);
        var legalRevision = await ApproveLoanAgreementAsync(agreements, lifecycle, graph, new IntercompanyAgreementDraftDto("IC-ACCRUAL-LOAN",
            "Synthetic intercompany loan", Lender, "Lender", Borrower, "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 5, 1),
            Terms: terms, Evidence: [new("synthetic-retained-vault:loan-agreement")]));
        ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind, IReadOnlyList<IntercompanyPartyMappingDto> mappings)
        {
            var profile = IntercompanyArrangementTestData.Create(id, Organization, kind, Lender, Borrower, "USD",
                mappings, Guid.NewGuid(), ProfileStart, canonicalId: legalRevision.AgreementId, canonicalVersion: legalRevision.AgreementVersion);
            return new(profile.Arrangement with { CanonicalPayloadHash = legalRevision.PayloadHash }, legalRevision);
        }
        var principal = Profile(PrincipalProfile, IntercompanyAccountingTemplateKindDto.Loan,
            [new(Lender, LenderBook, "a-loan-recv", "a-loan-pay-unused", "a-cash"), new(Borrower, BorrowerBook, "b-loan-recv-unused", "b-loan-pay", "b-cash")]);
        var interest = Profile(InterestProfile, IntercompanyAccountingTemplateKindDto.LoanInterest,
            [new(Lender, LenderBook, "a-int-recv", "a-int-pay-unused", null, "a-int-income", "a-int-expense-unused"),
             new(Borrower, BorrowerBook, "b-int-recv-unused", "b-int-pay", null, "b-int-income-unused", "b-int-expense")]);
        var arrangements = new PostgresIntercompanyAccountingArrangementStore(structureOptions);
        foreach (var profile in new[] { principal, interest })
        {
            var approved = profile.Arrangement;
            var prepared = approved with { Version = 1, Status = IntercompanyAccountingArrangementStatusDto.Draft, ApprovedBy = null, ApprovedAt = null };
            await arrangements.AppendAsync(Tenant, 0, new(Guid.NewGuid(), new string('a', 64),
                IntercompanyAccountingArrangementActionDto.Create, "mapping-preparer", ProfileStart, null, prepared));
            await arrangements.AppendAsync(Tenant, 1, new(Guid.NewGuid(), new string('b', 64),
                IntercompanyAccountingArrangementActionDto.Submit, "mapping-preparer", ProfileStart, null,
                prepared with { Version = 2, Status = IntercompanyAccountingArrangementStatusDto.Submitted }));
            await arrangements.AppendAsync(Tenant, 2, new(Guid.NewGuid(), new string('c', 64),
                IntercompanyAccountingArrangementActionDto.Approve, "mapping-reviewer", ProfileStart, null, approved));
        }
        var profiles = new IntercompanyAccountingArrangementService(arrangements, Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(),
            new IntercompanyAccountingArrangementLegalAuthority(agreements));

        var intercompany = new IntercompanyLedgerPostingAuthority(arrangements, profiles, bindings, database.AccountingConfigurationStore, plain, owners);
        // No fund cash accounts take part in this loan; the organization graph is read from the durable store.
        var accounts = new Mock<IFundAccountService>();
        accounts.Setup(service => service.QueryAccountsAsync(It.IsAny<AccountStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountSummaryDto>());
        var structures = new PostgresFundStructureService(graph, accounts.Object, new FundStructurePolicyService(),
            tenantAccessor: tenants, entityLifecycleStore: lifecycle);
        var legalPosting = new PostgresIntercompanyAgreementPostingAuthority(agreements);
        PostgresLedgerJournalStore? journals = null;
        var entityLifecycle = new LifecycleLedgerPostingAuthority(structures, lifecycle, () => journals, graph.AcquireConsolidationAuthorityLeaseAsync);
        journals = new PostgresLedgerJournalStore(database.Options, tenants,
            postingAuthorities: () => [entityLifecycle, intercompany],
            controlPreflights: () => [entityLifecycle, intercompany],
            intercompanyAgreementAuthority: () => legalPosting);
        async Task<LedgerAccountingPeriod> Period(Guid book, int month) => await journals.SavePeriodAsync(new(Guid.NewGuid(), book, 2026, month,
            $"2026-{month:00}", new(2026, month, 1), new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1), "Open", ProfileStart, null, 0), 0);
        var lenderMay = await Period(LenderBook, 5);
        var lenderJune = await Period(LenderBook, 6);
        await Period(BorrowerBook, 5);
        var borrowerJune = await Period(BorrowerBook, 6);

        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(service => service.GetWorkspaceAsync(LenderFund, LenderBook, It.IsAny<CancellationToken>(), Tenant, Company)).ReturnsAsync(lenderWorkspace);
        configuration.Setup(service => service.GetWorkspaceAsync(BorrowerFund, BorrowerBook, It.IsAny<CancellationToken>(), Tenant, Company)).ReturnsAsync(borrowerWorkspace);
        var drafts = new FileManualJournalEntryDraftStore(draftPath);
        var target = new DurableLedgerPostingTarget(journals);
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration.Object, new InMemoryAccountingActionAuditStore(),
            journalStore: journals, postingTarget: target, intercompanyPostingAuthority: intercompany);
        var proposals = new PostgresIntercompanyProposalStore(database.Options);
        var delivery = new IntercompanyProposalDeliveryService(proposals, profiles, journals, bindings, configuration.Object, drafts, workbench, owners,
            new Mock<IConsolidationFxAuthority>(MockBehavior.Strict).Object);
        var accrual = new IntercompanyAccrualDraftService(profiles, journals, bindings, configuration.Object, drafts, workbench,
            new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly])));
        return new(journals, proposals, drafts, workbench, delivery, accrual, target, lenderMay, lenderJune, borrowerJune, principal, bound["a-loan-recv"],
            legalRevision, lifecycle, structureOptions, draftPath);
    }

    /// <summary>Creates, submits and approves the loan through the native registry service with separate author and reviewer.</summary>
    private static async Task<IntercompanyAgreementRevisionDto> ApproveLoanAgreementAsync(PostgresIntercompanyAgreementStore store,
        PostgresEntityLifecycleStore lifecycle, PostgresFundStructureStore graph, IntercompanyAgreementDraftDto draft)
    {
        var service = new IntercompanyAgreementService(store, new SyntheticAgreementAccess(), new SyntheticAgreementEvidence(),
            new AgreementClock(), lifecycle, graph);
        var author = new IntercompanyAgreementCallContext(Tenant, "legal-author");
        var reviewer = new IntercompanyAgreementCallContext(Tenant, "legal-reviewer");
        var agreementId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        async Task<IntercompanyAgreementCommandDto> Command(IntercompanyAgreementDraftDto? content = null) => new(Guid.NewGuid(), agreementId,
            (await store.GetAsync(Tenant, agreementId))?.AggregateVersion ?? 0, proposalId, content, "Reviewed synthetic intercompany loan terms");
        await service.CreateAsync(author, await Command(draft));
        var submitted = await service.SubmitAsync(author, await Command());
        await service.ApproveAsync(reviewer, await Command() with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash });
        return Assert.Single((await store.GetAsync(Tenant, agreementId))!.Revisions);
    }

    private static async Task DropAsync(FundStructureStoreOptions structureOptions, string draftPath)
    {
        await using (var connection = new Npgsql.NpgsqlConnection(structureOptions.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new Npgsql.NpgsqlCommand($"drop schema if exists \"{structureOptions.Schema}\" cascade", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        var directory = Path.GetDirectoryName(draftPath)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static ChartOfAccountsNodeDto Node(string id, string root, string name, string type, IntercompanyAccountRoleDto? role)
        => new(id, $"{root}:{name}", name, type, IntercompanyRole: role);

    private static AccountingConfigurationWorkspaceDto Workspace(string fund, Guid book, IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => new(fund, book, AccountingConfigurationStatusDto.Active, "accrual-fixture-v1", Reviewed, [], chart, [], [], [], [], TenantId: Tenant, CompanyId: Company);

    private sealed record Fixture(PostgresLedgerJournalStore Journals, PostgresIntercompanyProposalStore Proposals,
        FileManualJournalEntryDraftStore Drafts, ManualJournalEntryWorkbenchService Workbench, IntercompanyProposalDeliveryService Delivery,
        IntercompanyAccrualDraftService Accrual, DurableLedgerPostingTarget Target, LedgerAccountingPeriod LenderMay, LedgerAccountingPeriod LenderJune,
        LedgerAccountingPeriod BorrowerJune, ResolvedIntercompanyAccountingArrangementDto Principal, IntercompanyAccountBindingDto LoanReceivable,
        IntercompanyAgreementRevisionDto LegalRevision, PostgresEntityLifecycleStore Lifecycle, FundStructureStoreOptions StructureOptions,
        string DraftPath) : IAsyncDisposable
    {
        /// <summary>The same durable draft file opened by another process or after a restart.</summary>
        public FileManualJournalEntryDraftStore ReopenDrafts() => new(DraftPath);

        /// <summary>Appends the next complete lifecycle timeline: active since formation, dissolved from <paramref name="at"/>.</summary>
        public async Task DissolveAsync(Guid entity, DateTimeOffset at)
        {
            var current = (await Lifecycle.GetLatestAsync(Tenant, entity))!;
            await Lifecycle.AppendAsync(EntityLifecycleRevisionIntegrity.Seal(new(Tenant, entity, current.Revision + 1, at, "lifecycle-reviewer",
                "Evidenced synthetic dissolution", "document://synthetic-dissolution", current.ContentHash, "",
                [new(Guid.NewGuid(), Formed, at, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation),
                 new(Guid.NewGuid(), at, null, LegalEntityLifecycleStatusDto.Dissolved, LegalEntityLifecycleEventKindDto.Dissolution)])), current.Revision);
        }

        public async ValueTask DisposeAsync() => await DropAsync(StructureOptions, DraftPath);

        /// <summary>The lender's posted loan advance under the reviewed Loan profile.</summary>
        public LedgerJournalEntryWrite Drawdown(decimal amount)
        {
            var id = Guid.NewGuid();
            var date = new DateOnly(2026, 5, 15);
            var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            const string description = "Synthetic intercompany loan drawdown";
            var dimensions = new LedgerLineDimensionSet(EntityId: Lender.ToString("D"), BookId: LenderBook.ToString("D"), CounterpartyId: Borrower.ToString("D"))
            {
                IntercompanyAgreementId = Principal.CanonicalAgreementId,
                IntercompanyAgreementVersion = Principal.CanonicalAgreementVersion,
                AccountingArrangementId = Principal.ArrangementId.ToString("D"),
                AccountingArrangementRevisionId = Principal.RevisionId.ToString("D"),
                IntercompanyTransactionId = "drawdown-20260515",
                ChartAccountNodeId = LoanReceivable.ChartAccountNodeId,
                AccountBindingRevisionId = LoanReceivable.RevisionId.ToString("D")
            };
            var entry = new JournalEntry(id, at, description,
                [new(Guid.NewGuid(), id, at, new(LoanReceivable.AccountName, LedgerAccountType.Asset), amount, 0m, description, dimensions),
                 new(Guid.NewGuid(), id, at, new("Cash", LedgerAccountType.Asset), 0m, amount, description)],
                new JournalEntryMetadata(EffectiveDate: date, Tags: new Dictionary<string, string> { ["companyId"] = Company }));
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), LenderBook, LenderMay.PeriodId, date, at, id.ToString("D"),
                ApprovalState: AccountingPostingApprovalStateDto.Approved, OperatorRationale: "Synthetic reviewed drawdown", LedgerBookId: LenderBook)
            { Actor = "synthetic-reviewer" };
            return new(entry, LenderBook, LenderMay.PeriodId, command.CommandId, PostingCommand: command, LedgerBookId: LenderBook);
        }

        /// <summary>Ordinary journal review: the preparer submits, a different person approves and posts.</summary>
        public async Task<ManualJournalEntryDraftDto> ReviewAndPostAsync(ManualJournalEntryDraftDto draft, string fund, Guid book,
            LedgerAccountingPeriod period, string preparer, string reviewer, string? submitter = null)
        {
            ManualJournalEntryDraftDto submitted;
            try
            {
                submitted = await Workbench.SubmitApprovalAsync(new(draft.JournalEntryId, fund, submitter ?? preparer, draft.Version,
                    LedgerBookId: book, TenantId: Tenant, CompanyId: Company));
            }
            catch (InvalidOperationException refusal)
            {
                var validated = await Workbench.ValidateDraftAsync(new(draft, submitter ?? preparer, LedgerBookId: book, TenantId: Tenant, CompanyId: Company));
                throw new Xunit.Sdk.XunitException(refusal.Message + " | " + string.Join(" | ",
                    validated.ValidationIssues.Select(issue => $"{issue.Severity} {issue.Code}: {issue.Message}")));
            }
            Assert.Equal(ManualJournalEntryStatusDto.Submitted, submitted.Status);
            var approved = (await Workbench.ApplyLifecycleActionAsync(Lifecycle(submitted, JournalEntryLifecycleActionDto.Approve, "approval"))).JournalEntry;
            Assert.Equal(ManualJournalEntryStatusDto.Approved, approved.Status);
            var posted = (await Workbench.ApplyLifecycleActionAsync(Lifecycle(approved, JournalEntryLifecycleActionDto.Post, "posting"))).JournalEntry;
            Assert.Equal(ManualJournalEntryStatusDto.Posted, posted.Status);
            return posted;

            JournalEntryLifecycleActionRequestDto Lifecycle(ManualJournalEntryDraftDto current, JournalEntryLifecycleActionDto action, string intent)
                => new(current.JournalEntryId, fund, action, reviewer, current.Version, Notes: "Reviewed synthetic loan-interest accrual against approved terms.",
                    EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/{intent}/ledger-book/{book:D}/{period.PeriodId:D}?tenantId={Tenant}&companyId={Company}"],
                    LedgerBookId: book, TenantId: Tenant, CompanyId: Company);
        }
    }

    private sealed class TenantAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => Tenant;
    }

    /// <summary>The caller's workstation scope already admits both synthetic entities in the one tenant.</summary>
    private sealed class SyntheticAgreementAccess : IIntercompanyAgreementAccessAuthority
    {
        public Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> entities, CancellationToken ct = default)
            => Task.FromResult(context.TenantId == Tenant && entities.All(entity => entity == Lender || entity == Borrower));
    }

    /// <summary>The evidence vault returns a retained hash for the signed agreement document.</summary>
    private sealed class SyntheticAgreementEvidence : IIntercompanyAgreementEvidenceAuthority
    {
        public Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(IntercompanyAgreementCallContext context,
            Guid agreementId, Guid proposalId, IReadOnlyList<IntercompanyAgreementEvidenceDto> evidence, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(evidence.Select(item =>
                new IntercompanyAgreementEvidenceDto(item.Reference, "synthetic-document", "synthetic-retained-loan-agreement-hash")).ToArray());
    }

    /// <summary>The loan was approved the day before its terms take effect.</summary>
    private sealed class AgreementClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 4, 30, 9, 0, 0, TimeSpan.Zero);
    }
}
