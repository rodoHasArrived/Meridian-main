using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
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
/// The ledger, proposal outbox, account bindings, chart and book ownership are the PostgreSQL stores and the
/// intercompany posting authority is the production class. The legal agreement registry, the accounting
/// profile history, entity lifecycle and journal drafts are synthetic in-memory stand-ins.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IntercompanyAccrualPostAndDeliverPostgresTests
{
    private const string Tenant = "tenant-a";
    private const string Company = "company-a";
    private const string LenderFund = "accrual-lender-fund";
    private const string BorrowerFund = "accrual-borrower-fund";
    private const string Transaction = "loan-interest:20260601-20260701";
    private static readonly Guid Lender = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid Borrower = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid LenderBook = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid BorrowerBook = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid PrincipalProfile = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid InterestProfile = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset ProfileStart = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reviewed = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [LedgerDatabaseFact]
    public async Task ReviewedAccrual_PostsAtSource_DeliversOneCounterpartDraft_AndPostsAtDestinationWithoutMirroringBack()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
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

    private static async Task<Fixture> SetupAsync(LedgerPostgresTestDatabase database)
    {
        var tenants = new TenantAccessor();
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

        // One approved legal loan agreement with structured fixed-rate terms, pinned by both accounting profiles.
        var terms = new IntercompanyAgreementTermsDto
        {
            StructuredTerms = new(new(new(2026, 5, 1), new(2027, 5, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple)
        };
        ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind, IReadOnlyList<IntercompanyPartyMappingDto> mappings)
        {
            var profile = IntercompanyArrangementTestData.Create(id, Guid.Parse("d1111111-1111-1111-1111-111111111111"), kind, Lender, Borrower, "USD",
                mappings, Guid.NewGuid(), ProfileStart);
            return profile with { LegalRevision = profile.LegalRevision with { Terms = profile.LegalRevision.Terms with { PartyOneRole = "Lender", PartyTwoRole = "Borrower", Terms = terms } } };
        }
        var principal = Profile(PrincipalProfile, IntercompanyAccountingTemplateKindDto.Loan,
            [new(Lender, LenderBook, "a-loan-recv", "a-loan-pay-unused", "a-cash"), new(Borrower, BorrowerBook, "b-loan-recv-unused", "b-loan-pay", "b-cash")]);
        var interest = Profile(InterestProfile, IntercompanyAccountingTemplateKindDto.LoanInterest,
            [new(Lender, LenderBook, "a-int-recv", "a-int-pay-unused", null, "a-int-income", "a-int-expense-unused"),
             new(Borrower, BorrowerBook, "b-int-recv-unused", "b-int-pay", null, "b-int-income-unused", "b-int-expense")]);
        var legalRevision = interest.LegalRevision;
        var arrangements = new InMemoryIntercompanyAccountingArrangementStore();
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
        var legal = IntercompanyArrangementTestData.LegalAuthority(interest);
        var legalLease = new Mock<IIntercompanyAccountingArrangementLegalLease>();
        legalLease.Setup(lease => lease.ResolveExact(interest.Arrangement.CanonicalReference)).Returns(legalRevision);
        legalLease.Setup(lease => lease.ResolveEffective(interest.Arrangement.CanonicalReference, It.IsAny<DateOnly>())).Returns(legalRevision);
        legal.Setup(authority => authority.AcquireLeaseAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<IntercompanyCanonicalAgreementReferenceDto>>(),
            It.IsAny<LegalEntityReadAuthorityProof?>(), It.IsAny<CancellationToken>())).ReturnsAsync(legalLease.Object);
        var profiles = new IntercompanyAccountingArrangementService(arrangements, Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(), legal.Object);

        var intercompany = new IntercompanyLedgerPostingAuthority(arrangements, profiles, bindings, database.AccountingConfigurationStore, plain, owners);
        var journals = new PostgresLedgerJournalStore(database.Options, tenants,
            postingAuthorities: () => [new SyntheticLifecycle(), intercompany],
            controlPreflights: () => [intercompany],
            intercompanyAgreementAuthority: () => new SyntheticLegalRegistry(legalRevision));
        async Task<LedgerAccountingPeriod> Period(Guid book, int month) => await journals.SavePeriodAsync(new(Guid.NewGuid(), book, 2026, month,
            $"2026-{month:00}", new(2026, month, 1), new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1), "Open", ProfileStart, null, 0), 0);
        var lenderMay = await Period(LenderBook, 5);
        var lenderJune = await Period(LenderBook, 6);
        await Period(BorrowerBook, 5);
        var borrowerJune = await Period(BorrowerBook, 6);

        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(service => service.GetWorkspaceAsync(LenderFund, LenderBook, It.IsAny<CancellationToken>(), Tenant, Company)).ReturnsAsync(lenderWorkspace);
        configuration.Setup(service => service.GetWorkspaceAsync(BorrowerFund, BorrowerBook, It.IsAny<CancellationToken>(), Tenant, Company)).ReturnsAsync(borrowerWorkspace);
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var target = new DurableLedgerPostingTarget(journals);
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration.Object, new InMemoryAccountingActionAuditStore(),
            journalStore: journals, postingTarget: target, intercompanyPostingAuthority: intercompany);
        var proposals = new PostgresIntercompanyProposalStore(database.Options);
        var delivery = new IntercompanyProposalDeliveryService(proposals, profiles, journals, bindings, configuration.Object, drafts, workbench, owners,
            new Mock<IConsolidationFxAuthority>(MockBehavior.Strict).Object);
        var accrual = new IntercompanyAccrualDraftService(profiles, journals, bindings, configuration.Object, drafts, workbench,
            new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly])));
        return new(journals, proposals, drafts, workbench, delivery, accrual, target, lenderMay, lenderJune, borrowerJune, principal, bound["a-loan-recv"]);
    }

    private static ChartOfAccountsNodeDto Node(string id, string root, string name, string type, IntercompanyAccountRoleDto? role)
        => new(id, $"{root}:{name}", name, type, IntercompanyRole: role);

    private static AccountingConfigurationWorkspaceDto Workspace(string fund, Guid book, IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => new(fund, book, AccountingConfigurationStatusDto.Active, "accrual-fixture-v1", Reviewed, [], chart, [], [], [], [], TenantId: Tenant, CompanyId: Company);

    private sealed record Fixture(PostgresLedgerJournalStore Journals, PostgresIntercompanyProposalStore Proposals,
        InMemoryManualJournalEntryDraftStore Drafts, ManualJournalEntryWorkbenchService Workbench, IntercompanyProposalDeliveryService Delivery,
        IntercompanyAccrualDraftService Accrual, DurableLedgerPostingTarget Target, LedgerAccountingPeriod LenderMay, LedgerAccountingPeriod LenderJune,
        LedgerAccountingPeriod BorrowerJune, ResolvedIntercompanyAccountingArrangementDto Principal, IntercompanyAccountBindingDto LoanReceivable)
    {
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

    /// <summary>Stands in for the native legal agreement registry: one approved revision, retained under a lease.</summary>
    private sealed class SyntheticLegalRegistry(IntercompanyAgreementRevisionDto revision) : IIntercompanyAgreementPostingAuthority
    {
        public Task<IAsyncDisposable> AcquireValidatedLeaseAsync(IntercompanyAgreementPostingRequest request, CancellationToken ct = default)
        {
            Assert.Equal(Tenant, request.TenantId);
            Assert.All(request.Lines, line => Assert.Equal((revision.AgreementId, revision.AgreementVersion), (line.AgreementId, line.AgreementVersion)));
            return Task.FromResult<IAsyncDisposable>(new Lease(revision));
        }

        private sealed class Lease(IntercompanyAgreementRevisionDto revision) : IIntercompanyAgreementPostingLease
        {
            public IReadOnlyList<IntercompanyAgreementStoreState> Agreements { get; } = [new(revision.AgreementId, 1, [], [revision], [], [])];
            public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<LegalEntitySummaryDto?>(null);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Entity lifecycle is covered by its own fixtures; both synthetic entities are treated as active here.</summary>
    private sealed class SyntheticLifecycle : ILedgerPostingAuthority
    {
        public string ControlName => "entity-lifecycle";
        public Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
        {
            foreach (var context in contexts)
                context.Decisions.Add(new(ControlName, "synthetic-fixture", true, "Synthetic active-entity authority for the accrual post-and-deliver fixture."));
            return Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance);
        }
    }
}
