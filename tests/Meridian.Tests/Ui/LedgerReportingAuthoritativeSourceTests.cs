using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Tests.Storage;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerReportingAuthoritativeSourceTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 15);
    private static readonly DateTimeOffset CutoffUtc = new(
        AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
        TimeSpan.Zero);

    [Fact]
    public async Task CaptureAsync_AppliesExactAsOfAndDimensions_FiltersMixedLegs_ExcludesFutureJournal_AndIsStable()
    {
        var fixture = CreateFixture();
        var included = Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-b");
        var future = Record(fixture, new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero), 12);
        fixture.JournalStore.Records.AddRange([included, future]);

        var first = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var second = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        first.DatasetRows.Should().ContainSingle();
        first.DatasetRows.Should().OnlyContain(row => row["journalEntryId"] == included.Entry.JournalEntryId.ToString("D"));
        first.Checkpoint.JournalEntryCount.Should().Be(1);
        first.Checkpoint.HighestGlobalSequence.Should().Be(11);
        first.Checkpoint.CheckpointId.Should().Be(second.Checkpoint.CheckpointId);
        first.Checkpoint.CheckpointHash.Should().Be(second.Checkpoint.CheckpointHash);

        fixture.JournalStore.LastQuery.Should().NotBeNull();
        fixture.JournalStore.LastQuery!.OccurredTo.Should().Be(CutoffUtc);
        fixture.JournalStore.LastQuery.LedgerBookId.Should().Be(fixture.Book.LedgerBookId);
        fixture.JournalStore.LastQuery.PeriodId.Should().Be(fixture.Period.PeriodId);
        fixture.JournalStore.LastQuery.LineDimensions.Should().BeEquivalentTo(new LedgerLineDimensionSet(
            fixture.FundId,
            CostCenterId: "cost-center-a",
            OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D")));
        fixture.LastStructureQuery.Should().NotBeNull();
        fixture.LastStructureQuery!.ActiveOnly.Should().BeTrue();
        fixture.LastStructureQuery.AsOf.Should().Be(CutoffUtc);
        fixture.LastStructureQuery.NodeId.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAsync_WithRealFundStructureService_RetainsAncestorHierarchyAndExcludesFutureOrganization()
    {
        var fixture = CreateFixture();
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var businessId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(
            fixture.OrganizationId,
            "REPORTING-ORG",
            "Reporting organization",
            "USD",
            effectiveFrom,
            "test"));
        await structure.CreateBusinessAsync(new CreateBusinessRequest(
            businessId,
            fixture.OrganizationId,
            BusinessKindDto.FundManager,
            "REPORTING-MANAGER",
            "Reporting manager",
            "USD",
            effectiveFrom,
            "test"));
        await structure.CreateFundAsync(new CreateFundRequest(
            fixture.FundNodeId,
            "REPORTING-FUND",
            "Reporting fund",
            "USD",
            effectiveFrom,
            "test",
            BusinessId: businessId));

        // A second hierarchy that becomes effective after the report cut must not become a
        // competing ancestor or influence the certified checkpoint.
        var futureOrganizationId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(
            futureOrganizationId,
            "FUTURE-ORG",
            "Future organization",
            "USD",
            CutoffUtc.AddTicks(1),
            "test"));

        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fixture.FundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(
                fixture.FundId,
                fixture.TenantId,
                fixture.CompanyId));
        fixture.Source = new LedgerReportingAuthoritativeSource(
            fixture.JournalStore,
            tenancy,
            structure,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero)));
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11));

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        capture.Checkpoint.OrganizationId.Should().Be(fixture.OrganizationId.ToString("D"));
        capture.Checkpoint.CompanyId.Should().Be(fixture.CompanyId);
        capture.Checkpoint.FundId.Should().Be(fixture.FundId);
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.Contains(futureOrganizationId.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task CaptureAsync_FinalReportRejectsSoftClosedPeriod()
    {
        var fixture = CreateFixture(periodStatus: "SoftClosed");
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*SoftClosed*not HardClosed*");
    }

    [Fact]
    public async Task CaptureAsync_FailsClosedWhenStoreReturnsLineOutsideRequestedDimension()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.ApplyQueryFilters = false;
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            fundId: "another-fund"));

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*dimension*does not match certified value*");
    }

    [Fact]
    public async Task CaptureAsync_FailsClosedWhenSelectedEntityIsOnlyEffectiveAfterAsOfCut()
    {
        var fixture = CreateFixture();
        var futureEntityId = Guid.NewGuid();
        fixture.Parameters = fixture.Parameters with
        {
            Scope = fixture.Parameters.Scope with
            {
                EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                EntityId = futureEntityId.ToString("D")
            },
            ConsolidationLevel = ReportingConsolidationLevelDto.Entity
        };

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*not present in the authoritative as-of structure graph*");
        fixture.LastStructureQuery.Should().Match<OrganizationStructureQuery>(query =>
            query.ActiveOnly && query.AsOf == CutoffUtc);
    }

    [Theory]
    [InlineData(ReportingOutputFormatDto.Pdf)]
    [InlineData(ReportingOutputFormatDto.Xlsx)]
    [InlineData(ReportingOutputFormatDto.ClientPackage)]
    public async Task CaptureAsync_CapitalAccountPrimaryDocument_ShouldBindPartnersCapitalToCompleteAsOfLedgerHistory(
        ReportingOutputFormatDto outputFormat)
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = outputFormat
        };
        var priorPeriodId = Guid.NewGuid();
        fixture.JournalStore.Records.Add(
            Record(
                fixture,
                new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
                10,
                debitCostCenterId: "cost-center-a",
                creditCostCenterId: "cost-center-a") with
            {
                PeriodId = priorPeriodId
            });
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        var templateNeutralCapture =
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement"));

        capture.DatasetRows.Should().HaveCount(2);
        capture.Checkpoint.CheckpointId.Should().Be(templateNeutralCapture.Checkpoint.CheckpointId);
        capture.Checkpoint.CheckpointHash.Should().Be(
            templateNeutralCapture.Checkpoint.CheckpointHash,
            "the durable source checkpoint must remain independent of the output template");
        capture.CertifiedLedgerPresentation.Should().NotBeNull();
        var presentation = capture.CertifiedLedgerPresentation!;
        presentation.SourceCheckpointId.Should().Be(capture.Checkpoint.CheckpointId);
        presentation.SourceCheckpointHash.Should().Be(capture.Checkpoint.CheckpointHash);
        var partnersCapital = presentation.ReportPack.Statements.PartnersCapital;
        partnersCapital.Should().NotBeNull();
        partnersCapital!.BeginningCapital.Should().Be(125m);
        partnersCapital.EndingCapital.Should().Be(250m);
        partnersCapital.IsReconciled.Should().BeTrue();
        presentation.ReportPack.IsBalanced.Should().BeTrue();
        capture.Checkpoint.EvidenceIds.Should().Contain(
            $"ledger-report-pack:{presentation.ReportPack.Request.ReportId}:{presentation.ReportPack.Signature.PayloadChecksumSha256}");
        capture.Checkpoint.EvidenceIds.Should().ContainSingle(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.LastQuery.Should().NotBeNull();
        fixture.JournalStore.LastQuery!.PeriodId.Should().BeNull(
            "the presentation must include retained history before the reporting period");
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_CapitalAccountCsv_DoesNotBuildLedgerDocumentPresentation()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = ReportingOutputFormatDto.Csv
        };
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement"));

        capture.CertifiedLedgerPresentation.Should().BeNull();
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(0);
    }

    [Fact]
    public async Task CaptureAsync_NonCapitalClientPackage_DoesNotRequireCompleteHistoryReplay()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = ReportingOutputFormatDto.ClientPackage
        };
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));
        fixture.JournalStore.RejectCompleteHistoryQueries = true;

        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("investor-statement"));

        capture.DatasetRows.Should().HaveCount(2);
        capture.CertifiedLedgerPresentation.Should().BeNull();
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.QueryCount.Should().Be(1);
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("basis")]
    public async Task CaptureAsync_CanonicalDisposalEvidenceBlocksAndRestoringItProducesSignedLotEvidence(string fault)
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var lot = CanonicalOpenLotConsumerTests.DurableLot(1) with { LedgerBookId = fixture.Book.LedgerBookId };
        var sourceRecord = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        var disposal = CanonicalOpenLotConsumerTests.DisposalJournal(lot);
        var dimensions = sourceRecord.Entry.Lines[0].Dimensions! with
        {
            InstrumentId = lot.SecurityId,
            PositionId = lot.BookPositionId
        };
        var entry = new JournalEntry(disposal.JournalEntryId, disposal.Timestamp, disposal.Description,
            disposal.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                line.Account, line.Debit, line.Credit, line.Description, dimensions)).ToArray());
        fixture.JournalStore.Records.Add(sourceRecord with { Entry = entry });
        var history = CanonicalOpenLotConsumerTests.History(lot, entry, lot.ToOpenLot());
        fixture.JournalStore.Disposals.Add(fault switch
        {
            "missing" => history with { CanonicalLots = null },
            "scope" => history with { CanonicalLots = [history.CanonicalLots![0] with { BookPositionId = Guid.NewGuid() }] },
            _ => history with { Lots = [history.Lots[0] with { CostBasis = 301m }] }
        });
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");
        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>().WithMessage("*blocks canonical reporting*");

        fixture.JournalStore.Disposals[0] = history;
        var restored = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        var artifact = restored.CertifiedLedgerPresentation!.ReportPack.Artifacts
            .Single(item => item.Name == "canonical-open-lot-evidence.json");
        artifact.Content.Should().Contain(lot.SecurityId.ToString("D"))
            .And.Contain("AcquisitionFxRateToFunctional").And.Contain("1.2")
            .And.Contain("FunctionalCostBasis");
        restored.Checkpoint.EvidenceIds.Should().Contain(reference => reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
    }

    private static Fixture CreateFixture(string periodStatus = "HardClosed")
    {
        const string tenantId = "tenant-reporting";
        const string companyId = "company-reporting";
        const string fundId = "fund-reporting";
        var organizationId = Guid.NewGuid();
        var fundNodeId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(
            bookId,
            fundId,
            fundNodeId,
            FundStructureNodeKindDto.Fund,
            "Primary book",
            "USD",
            now,
            now,
            AccountingBasis: AccountingBasisKindDto.Gaap);
        var period = new LedgerAccountingPeriod(
            periodId,
            bookId,
            2026,
            7,
            "2026-07",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            periodStatus,
            now,
            now,
            4);
        var journalStore = new QueryFilteringJournalStore(book, period);
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(fundId, tenantId, companyId));

        var structure = Substitute.For<IFundStructureService>();
        OrganizationStructureQuery? lastQuery = null;
        var graph = new OrganizationStructureGraphDto(
            [new OrganizationSummaryDto(
                organizationId,
                "ORG",
                "Reporting organization",
                "USD",
                true,
                now.AddYears(-1),
                null,
                [])],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [
                new FundStructureNodeDto(
                    organizationId,
                    FundStructureNodeKindDto.Organization,
                    "ORG",
                    "Reporting organization",
                    null,
                    true,
                    now.AddYears(-1),
                    null),
                new FundStructureNodeDto(
                    fundNodeId,
                    FundStructureNodeKindDto.Fund,
                    "FUND",
                    "Reporting fund",
                    null,
                    true,
                    now.AddYears(-1),
                    null)
            ],
            [new OwnershipLinkDto(
                Guid.NewGuid(),
                organizationId,
                fundNodeId,
                OwnershipRelationshipTypeDto.Owns,
                100m,
                true,
                now.AddYears(-1),
                null,
                null)],
            []);
        structure.GetOrganizationStructureAsync(
                Arg.Do<OrganizationStructureQuery>(query => lastQuery = query),
                Arg.Any<CancellationToken>())
            .Returns(graph);

        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(
                fundId,
                Dimensions: new LedgerDimensionSetDto(CostCenterId: "cost-center-a")),
            periodId.ToString("D"),
            AsOfDate,
            new ReportingLedgerBookSelectionDto(bookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Csv,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var fixture = new Fixture(
            tenantId,
            companyId,
            fundId,
            organizationId,
            fundNodeId,
            book,
            period,
            journalStore,
            parameters,
            new ReportAccessQueryContext(
                "reporting-user",
                CompanyId: companyId,
                TenantId: tenantId,
                RequireBoundScope: true));
        fixture.Source = new LedgerReportingAuthoritativeSource(
            journalStore,
            tenancy,
            structure,
            new FixedTimeProvider(now));
        fixture.StructureQuery = () => lastQuery;
        return fixture;
    }

    private static LedgerJournalEntryRecord Record(
        Fixture fixture,
        DateTimeOffset timestamp,
        long sequence,
        string? fundId = null,
        string? debitCostCenterId = "cost-center-a",
        string? creditCostCenterId = "cost-center-a")
    {
        var journalId = Guid.NewGuid();
        var debitDimensions = new LedgerLineDimensionSet(
            fundId ?? fixture.FundId,
            CostCenterId: debitCostCenterId,
            OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D"));
        var creditDimensions = debitDimensions with { CostCenterId = creditCostCenterId };
        var entry = new JournalEntry(
            journalId,
            timestamp,
            "Reporting source line",
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Assets:Cash", LedgerAccountType.Asset),
                    125m,
                    0m,
                    "Reporting source line",
                    debitDimensions),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Equity:Capital", LedgerAccountType.Equity),
                    0m,
                    125m,
                    "Reporting source line",
                    creditDimensions)
            ]);
        return new LedgerJournalEntryRecord(
            entry,
            Guid.NewGuid(),
            fixture.Period.PeriodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: sequence,
            CreatedAt: timestamp,
            AccountingBasis: AccountingBasisKindDto.Gaap);
    }

    private sealed class Fixture(
        string tenantId,
        string companyId,
        string fundId,
        Guid organizationId,
        Guid fundNodeId,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        QueryFilteringJournalStore journalStore,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext access)
    {
        public string TenantId { get; } = tenantId;
        public string CompanyId { get; } = companyId;
        public string FundId { get; } = fundId;
        public Guid OrganizationId { get; } = organizationId;
        public Guid FundNodeId { get; } = fundNodeId;
        public LedgerBookRecord Book { get; } = book;
        public LedgerAccountingPeriod Period { get; } = period;
        public QueryFilteringJournalStore JournalStore { get; } = journalStore;
        public ReportingRunParametersDto Parameters { get; set; } = parameters;
        public ReportAccessQueryContext Access { get; } = access;
        public LedgerReportingAuthoritativeSource Source { get; set; } = null!;
        public Func<OrganizationStructureQuery?> StructureQuery { private get; set; } = null!;
        public OrganizationStructureQuery? LastStructureQuery => StructureQuery();
    }

    private sealed class QueryFilteringJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period) : ILedgerJournalStore, ILedgerTaxLotDisposalHistory
    {
        public List<LedgerJournalEntryRecord> Records { get; } = [];
        public List<LedgerTaxLotDisposalHistoryRecord> Disposals { get; } = [];
        public Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
            Guid ledgerBookId, IReadOnlyList<Guid> journalEntryIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>(Disposals);
        public bool ApplyQueryFilters { get; set; } = true;
        public bool RejectCompleteHistoryQueries { get; set; }
        public int QueryCount { get; private set; }
        public int CompleteHistoryQueryCount { get; private set; }
        public LedgerJournalEntryQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            LastQuery = query;
            QueryCount++;
            if (query.PeriodId is null)
            {
                CompleteHistoryQueryCount++;
                if (RejectCompleteHistoryQueries)
                {
                    throw new NotSupportedException(
                        "Complete-history replay is intentionally unavailable in this test.");
                }
            }
            IReadOnlyList<LedgerJournalEntryRecord> result = ApplyQueryFilters
                ? Records.Where(record =>
                        (query.PeriodId is null || record.PeriodId == query.PeriodId)
                        && (query.OccurredTo is null || record.Entry.Timestamp <= query.OccurredTo)
                        && record.Entry.Lines.Any(line => DimensionsMatch(line.Dimensions, query.LineDimensions)))
                    .ToArray()
                : Records.ToArray();
            return Task.FromResult(result);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(periodId == period.PeriodId ? period : null);

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Records.Where(record => record.PeriodId == periodId).ToArray());

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Records.Where(record => record.AggregateId == aggregateId).ToArray());

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([period]);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod value,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            Task.FromResult(value);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>([book]);

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord value, CancellationToken ct = default) =>
            Task.FromResult(value);

        private static bool DimensionsMatch(
            LedgerLineDimensionSet? actual,
            LedgerLineDimensionSet? expected) =>
            expected is null || actual is not null
                && Matches(actual.FundId, expected.FundId)
                && Matches(actual.OrganizationId, expected.OrganizationId)
                && Matches(actual.BookId, expected.BookId)
                && Matches(actual.CostCenterId, expected.CostCenterId);

        private static bool Matches(string? actual, string? expected) =>
            string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
