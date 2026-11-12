using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyBalanceScheduleTests
{
    [Fact]
    public async Task PostedReciprocalBalances_AreDerivedFromBothBooks_AndSourceChangesAlterHash()
    {
        var fixture = Fixture();
        var balanced = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.True(balanced.IsReady);
        Assert.Equal(2, balanced.Rows.Count);
        Assert.Equal(0m, balanced.Rows.Sum(row => row.TransactionSignedBalance));
        fixture.TargetRecords.Clear();
        var broken = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.True(broken.IsMappingComplete);
        Assert.False(broken.IsBalanced);
        Assert.NotEqual(balanced.ContentHash, broken.ContentHash);
    }

    [Fact]
    public async Task MissingLegacyAttribution_IsVisibleAndCannotBeReplacedByAgreementTerms()
    {
        var fixture = Fixture(legacy: true);
        var result = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.False(result.IsMappingComplete);
        Assert.Contains(result.Issues, issue => issue.Code == "UnmappedLegacyIntercompany");
        Assert.Equal(100m, Assert.Single(result.Rows).TransactionSignedBalance);
        fixture.SourceRecords.Clear();
        var empty = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.Empty(empty.Rows);
        Assert.True(empty.IsReady);
    }

    [Fact]
    public async Task MissingOrForeignScope_CannotProduceReadySchedule()
    {
        var fixture = Fixture();
        Assert.False((await fixture.Service.GetAsync(null, "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date)).Available);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync("tenant-b", "company-a", "source-fund",
            IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date));
    }

    [Fact]
    public async Task CanonicalLegacyNameWithoutChartRoleOrBinding_IsUnmappedExposureInsteadOfZero()
    {
        var fixture = Fixture(legacy: true, canonicalWithoutMetadata: true);
        Assert.Equal(ConsolidationService.ReceivableAccount, fixture.SourceRecords[0].Entry.Lines[0].Account.Name);
        Assert.Null(fixture.SourceRecords[0].Entry.Lines[0].Dimensions);
        var result = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund",
            IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.False(result.IsReady);
        Assert.False(result.IsMappingComplete);
        Assert.Equal(100m, Assert.Single(result.Rows).TransactionSignedBalance);
        Assert.False(result.Rows[0].MappingVerified);
        Assert.Contains(result.Issues, issue => issue.Code == "UnmappedLegacyIntercompany");
    }

    [Fact]
    public async Task ApprovedLegacySidecars_ResolvePostedBalancesWithoutRewritingJournals()
    {
        var fixture = Fixture(legacy: true, attributedLegacy: true);
        var result = await fixture.Service.GetAsync("tenant-a", "company-a", "source-fund", IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date);
        Assert.True(result.IsReady);
        Assert.Equal(2, result.LegacyAttributionIds.Count);
        Assert.All(result.Rows, row => Assert.True(row.MappingVerified));
        Assert.Null(fixture.SourceRecords[0].Entry.Lines[0].Dimensions!.AgreementId);
        Assert.Null(fixture.TargetRecords[0].Entry.Lines[0].Dimensions!.AgreementId);
    }

    internal static IntercompanyBalanceScheduleService ReadySchedule(string fund, Guid book, string tenant = "tenant-a", string company = "company-a")
        => Fixture(sourceFund: fund, sourceBookId: book, tenant: tenant, company: company).Service;

    private static ScheduleFixture Fixture(bool legacy = false, string sourceFund = "source-fund", Guid? sourceBookId = null, string tenant = "tenant-a", string company = "company-a", bool attributedLegacy = false,
        bool canonicalWithoutMetadata = false)
    {
        var sourceBook = new LedgerBookRecord(sourceBookId ?? IntercompanyProposalTestData.BookA, sourceFund, IntercompanyProposalTestData.EntityA,
            FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
        var targetBook = IntercompanyProposalTestData.TargetBook();
        var sourceChart = new ChartOfAccountsNodeDto("a-receivable", "Assets:Due from B", "Assets:Due from B", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable);
        var targetChart = IntercompanyProposalTestData.Chart().Single(x => x.NodeId == "b-payable");
        IntercompanyAccountBindingDto Binding(LedgerBookRecord book, ChartOfAccountsNodeDto chart) => new(Guid.NewGuid(), Guid.NewGuid(), 1,
            book.LedgerBookId, chart.NodeId, chart.IntercompanyRole!.Value, chart.Path, chart.AccountName, chart.AccountType,
            new(2026, 1, 1), null, [], tenant, company, "reviewer", IntercompanyProposalTestData.Now, "Synthetic mapping");
        var sourceBinding = Binding(sourceBook, sourceChart);
        var targetBinding = Binding(targetBook, targetChart);
        LedgerJournalEntryRecord Record(LedgerBookRecord book, ChartOfAccountsNodeDto chart, IntercompanyAccountBindingDto binding, bool debit)
        {
            var id = Guid.NewGuid();
            var dimensions = new LedgerLineDimensionSet(EntityId: book.FundStructureNodeId.ToString("D"),
                CounterpartyId: (debit ? IntercompanyProposalTestData.EntityB : IntercompanyProposalTestData.EntityA).ToString("D"))
            {
                ChartAccountNodeId = chart.NodeId, AccountBindingRevisionId = legacy ? null : binding.RevisionId.ToString("D"),
                AgreementId = legacy ? null : IntercompanyProposalTestData.AgreementId.ToString("D"),
                AgreementRevisionId = legacy ? null : IntercompanyProposalTestData.RevisionId.ToString("D")
            };
            var entry = new JournalEntry(id, IntercompanyProposalTestData.Now, "Posted original facts",
                [new(Guid.NewGuid(), id, IntercompanyProposalTestData.Now,
                    new(canonicalWithoutMetadata ? debit ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount : chart.AccountName,
                        debit ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                    debit ? 100m : 0m, debit ? 0m : 100m, "Posted original facts", canonicalWithoutMetadata ? null : dimensions),
                 new(Guid.NewGuid(), id, IntercompanyProposalTestData.Now, new("Assets:Cash", LedgerAccountType.Asset),
                     debit ? 0m : 100m, debit ? 100m : 0m, "Posted original facts")],
                new JournalEntryMetadata(EffectiveDate: IntercompanyProposalTestData.Date));
            return new(entry, book.LedgerBookId, Guid.NewGuid(), null, null, 1, IntercompanyProposalTestData.Now);
        }
        var sourceRecords = new List<LedgerJournalEntryRecord> { Record(sourceBook, sourceChart, sourceBinding, true) };
        var targetRecords = new List<LedgerJournalEntryRecord> { Record(targetBook, targetChart, targetBinding, false) };
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid book, CancellationToken _) => Task.FromResult<LedgerBookRecord?>(book == sourceBook.LedgerBookId ? sourceBook : targetBook));
        journals.Setup(x => x.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .Returns((LedgerJournalEntryQuery query, CancellationToken _) => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                (query.LedgerBookId == sourceBook.LedgerBookId ? sourceRecords : targetRecords).ToArray()));
        var bindings = new Mock<IIntercompanyAccountBindingStore>();
        bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
        bindings.Setup(x => x.HistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns((Guid book, CancellationToken _) =>
            Task.FromResult<IReadOnlyList<IntercompanyAccountBindingDto>>(canonicalWithoutMetadata ? [] : book == sourceBook.LedgerBookId ? [sourceBinding] : [targetBinding]));
        var configuration = new Mock<IAccountingConfigurationStore>();
        configuration.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>(), tenant, company))
            .Returns((string fund, Guid? book, CancellationToken _, string? scopeTenant, string? scopeCompany) => Task.FromResult<AccountingConfigurationWorkspaceDto?>(
                new(fund, book, AccountingConfigurationStatusDto.Active, "v1", IntercompanyProposalTestData.Now, [],
                    canonicalWithoutMetadata ? [] : book == sourceBook.LedgerBookId ? [sourceChart] : [targetChart], [], [], [], [])));
        var ownership = new Mock<IFundProfileTenancyRegistry>();
        ownership.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns((string fund, CancellationToken _) =>
            Task.FromResult<FundProfileOwnership?>(new(fund, tenant, company)));
        var agreements = new Mock<IIntercompanyAgreementStore>();
        agreements.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
        agreements.Setup(x => x.GetAsync(tenant, IntercompanyProposalTestData.AgreementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAgreementHistoryDto([new(Guid.NewGuid(), "synthetic", IntercompanyAgreementActionDto.Approve,
                "reviewer", IntercompanyProposalTestData.Now, "Synthetic approval", IntercompanyProposalTestData.Agreement() with { PartyMappings = [new(IntercompanyProposalTestData.EntityA, sourceBook.LedgerBookId, "a-receivable", "a-payable", "a-cash"), new(IntercompanyProposalTestData.EntityB, targetBook.LedgerBookId, "b-receivable", "b-payable", "b-cash")] })]));
        var agreementService = new IntercompanyAgreementService(agreements.Object, Mock.Of<IIntercompanyAgreementScopeAuthority>(), Mock.Of<IIntercompanyAgreementEvidenceAuthority>());
        var sidecars = new Mock<ILegacyIntercompanyAttributionStore>();
        sidecars.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
        LegacyIntercompanyAttributionDto Attribution(LedgerBookRecord book, LedgerJournalEntryRecord record, IntercompanyAccountBindingDto binding)
            => new(Guid.NewGuid(), 1, book.LedgerBookId, record.Entry.JournalEntryId, record.Entry.Lines[0].EntryId,
                LegacyIntercompanyAttributionRules.SourceDigest(book, record, record.Entry.Lines[0]),
                IntercompanyProposalTestData.AgreementId, IntercompanyProposalTestData.RevisionId, binding.RevisionId,
                binding.ChartAccountNodeId, "legacy-transaction", "preparer", "reviewer", IntercompanyProposalTestData.Now,
                "retained-migration-evidence", tenant, company);
        var sourceAttribution = Attribution(sourceBook, sourceRecords[0], sourceBinding);
        var targetAttribution = Attribution(targetBook, targetRecords[0], targetBinding);
        sidecars.Setup(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns((Guid book, CancellationToken _) =>
            Task.FromResult<IReadOnlyList<LegacyIntercompanyAttributionDto>>(book == sourceBook.LedgerBookId ? [sourceAttribution] : [targetAttribution]));
        return new(new(journals.Object, agreementService, bindings.Object, configuration.Object, ownership.Object, agreements.Object,
            attributedLegacy ? sidecars.Object : null), sourceRecords, targetRecords);
    }
    private sealed record ScheduleFixture(IntercompanyBalanceScheduleService Service, List<LedgerJournalEntryRecord> SourceRecords,
        List<LedgerJournalEntryRecord> TargetRecords);
}
