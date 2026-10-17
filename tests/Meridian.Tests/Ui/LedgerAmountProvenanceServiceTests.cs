using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerAmountProvenanceServiceTests
{
    private static readonly Guid AmountId = Guid.Parse("3ae0c5ed-cbe6-4975-93a7-84e59399aeaa");
    private static readonly Guid BookId = Guid.Parse("ef20c5ed-cbe6-4975-93a7-84e59399aeaa");
    private static readonly Guid PeriodId = Guid.Parse("aac0c5ed-cbe6-4975-93a7-84e59399aeaa");
    private static readonly LedgerAmountScopeDto Scope = new("tenant-a", "company-a", "fund-a", BookId, PeriodId);
    private static readonly ReconciliationBreakQueueScope Access = new(Scope.TenantId, Scope.CompanyId);
    private static readonly DateTimeOffset Captured = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task GetAsync_UsesRetainedIdsOnly_ExcludesReportWideAndUnrelatedSameNameAndSymbol()
    {
        var snapshot = Snapshot();
        var owned = Case("case-retained");
        var otherFund = Case("case-other-fund") with { FundProfileId = "fund-b" };
        var sameFundUnrelated = Case("case-unrelated");
        var service = Service(snapshot, owned, otherFund, sameFundUnrelated);
        var result = await Read(service, snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Warnings.Should().Contain(warning => warning.Contains("source content has not been verified"));
        result.Evidence.Select(item => item.EvidenceId).Should().BeEquivalentTo("entry-retained", "source-retained", "case-retained");
        result.Reconciliation.RelatedCaseIds.Should().Equal("case-retained");
        result.StrategyRuns.Should().BeEmpty();
        result.Evidence.Should().NotContain(item => item.EvidenceId == "report-wide" || item.EvidenceId == "artifact.json");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("period")]
    public async Task GetAsync_ExplicitCaseIdInForeignScope_BlocksAndSuppressesAllEvidence(string dimension)
    {
        var snapshot = Snapshot();
        var foreign = dimension switch
        {
            "tenant" => Case("case-retained") with { TenantId = "tenant-b" },
            "company" => Case("case-retained") with { CompanyId = "company-b" },
            "fund" => Case("case-retained") with { FundProfileId = "fund-b" },
            "book" => Case("case-retained") with { LedgerBookId = Guid.NewGuid() },
            _ => Case("case-retained") with { AccountingPeriodId = Guid.NewGuid().ToString("D") }
        };
        var result = await Read(Service(snapshot, foreign), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.Blocked);
        result.Evidence.Should().BeEmpty();
        result.Reconciliation.RelatedCaseIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_MissingExplicitCase_DoesNotSubstituteSameNameOrSymbol()
    {
        var snapshot = Snapshot();
        var result = await Read(Service(snapshot, Case("case-retained-suffix")), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Reconciliation.RelatedCaseIds.Should().BeEmpty();
        result.Evidence.Should().NotContain(item => item.EvidenceType == "reconciliation-case");
    }

    [Fact]
    public async Task GetAsync_ChangedCaseSnapshot_IsReviewRequiredWithoutCurrentCaseProof()
    {
        var snapshot = Snapshot();
        var result = await Read(Service(snapshot, Case("case-retained") with { LastUpdatedAt = Captured.AddSeconds(1) }), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Reconciliation.RelatedCaseIds.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Contains("changed"));
    }

    [Fact]
    public async Task GetAsync_DuplicateCaseIdentity_IsBlocked()
    {
        var snapshot = Snapshot();
        var result = await Read(Service(snapshot, Case("case-retained"), Case("case-retained")), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.Blocked);
        result.Evidence.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-pointer")]
    [InlineData("stale-hash")]
    [InlineData("duplicate-pointer")]
    [InlineData("duplicate-amount")]
    public async Task GetAsync_InvalidRetainedBinding_IsBlocked(string fault)
    {
        var snapshot = Snapshot();
        var pointers = snapshot.Provenance.LineagePointers.ToArray();
        snapshot = fault switch
        {
            "foreign-pointer" => snapshot with
            {
                Provenance = snapshot.Provenance with
                { LineagePointers = [pointers[0] with { AmountScope = Scope with { FundProfileId = "fund-b" } }, .. pointers.Skip(1)] }
            },
            "stale-hash" => snapshot with { Provenance = snapshot.Provenance with { SourceSnapshotHash = new string('b', 64) } },
            "duplicate-pointer" => snapshot with { Provenance = snapshot.Provenance with { LineagePointers = [.. pointers, pointers[0]] } },
            _ => snapshot with { Provenance = snapshot.Provenance with { LedgerAmounts = [.. snapshot.Provenance.LedgerAmounts, snapshot.Provenance.LedgerAmounts[0]] } }
        };
        var result = await Read(Service(snapshot, Case("case-retained")), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.Blocked);
        result.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_MissingReferenceAfterForeignCase_CannotDowngradeBlockedProof()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Provenance = snapshot.Provenance with
            {
                LineagePointers = [.. snapshot.Provenance.LineagePointers,
                Pointer("source-document", "missing-timestamp") with { CapturedAt = null },
                Pointer("reconciliation-case", "missing-case")]
            }
        };
        var result = await Read(Service(snapshot, Case("case-retained") with { FundProfileId = "fund-b" }), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.Blocked);
        result.Evidence.Should().BeEmpty();
        result.Reconciliation.RelatedCaseIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_MissingSupport_IsReviewRequired()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Provenance = snapshot.Provenance with
            { LineagePointers = snapshot.Provenance.LineagePointers.Where(item => item.EvidenceType != "source-document").ToArray() }
        };
        var result = await Read(Service(snapshot, Case("case-retained")), snapshot);
        result!.ProofStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Warnings.Should().Contain(warning => warning.Contains("supporting source evidence is missing"));
    }

    [Fact]
    public async Task GetAsync_LegacyTextKeysAndUnscopedRequests_FailClosed()
    {
        var snapshot = Snapshot();
        var service = Service(snapshot, Case("case-retained"));
        (await service.GetAsync(snapshot.ReportId, "Securities:AAPL", Access)).Should().BeNull();
#pragma warning disable CS0618
        (await service.GetAsync(snapshot.ReportId, AmountId.ToString("D"))).Should().BeNull();
#pragma warning restore CS0618
        snapshot = snapshot with { Provenance = snapshot.Provenance with { LedgerAmounts = [] } };
        (await Read(Service(snapshot), snapshot)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ForeignTenantAmount_DoesNotDiscloseIdentityOrValue()
    {
        var snapshot = Snapshot();
        var result = await Service(snapshot).GetAsync(snapshot.ReportId, AmountId.ToString("D"), new ReconciliationBreakQueueScope("tenant-b", "company-a"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_DoesNotScrapeProviderMetadataFromCaseProse()
    {
        var snapshot = Snapshot();
        var item = Case("case-retained") with { ExplainabilitySummary = "providerEventId=unrelated,securityId=foreign,cashAmount=999", CustodianId = "provider" };
        var result = await Read(Service(snapshot, item), snapshot);
        result!.Evidence.Should().NotContain(evidence => evidence.EvidenceType == "provider-event");
        result.Evidence.Should().OnlyContain(evidence => evidence.ProviderEventId == null && evidence.CashAmount == null);
    }

    private static Task<LedgerAmountProvenanceDetailDto?> Read(LedgerAmountProvenanceService service, FundReportPackSnapshotDto snapshot)
        => service.GetAsync(snapshot.ReportId, AmountId.ToString("D"), Access);

    private static LedgerAmountProvenanceService Service(FundReportPackSnapshotDto snapshot, params ReconciliationBreakQueueItem[] cases)
    {
        var reports = Substitute.For<IGovernanceReportPackRepository>();
        reports.GetAsync(snapshot.ReportId, Arg.Any<CancellationToken>()).Returns(snapshot);
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), null, Arg.Any<CancellationToken>()).Returns(cases);
        return new(reports, queue);
    }

    private static ReconciliationBreakQueueItem Case(string id)
        => new(id, "run-a", "Provider Securities AAPL", ReconciliationBreakCategory.AmountMismatch,
            ReconciliationBreakQueueStatus.Open, 25m, "Securities AAPL", null, Captured, Captured,
            ExplainabilitySummary: "Securities:AAPL, source-retained, entry-retained", RoutingDetail: "Securities:AAPL",
            LedgerBookId: BookId, AccountingPeriodId: PeriodId.ToString("D"))
        { TenantId = Scope.TenantId, CompanyId = Scope.CompanyId, FundProfileId = Scope.FundProfileId };

    private static FundReportPackLineagePointerDto Pointer(string type, string id)
        => new("line", "Securities:AAPL", type, id, DisplayLabel: "Securities AAPL", CapturedAt: Captured)
        { AmountId = AmountId, AmountScope = Scope, SourceSnapshotHash = Hash };

    private static FundReportPackSnapshotDto Snapshot()
        => new(Guid.NewGuid(), Scope.FundProfileId, "Retained report", GovernanceReportKindDto.TrialBalance,
            "USD", Captured, Captured.AddMinutes(1), 400m, "operator", "correlation", "period close",
            new FundReportPackProvenanceDto(["run-global"], 1, 1, 1, 1, 1, 1, 0,
                [Pointer("ledger-account", "entry-retained"), Pointer("source-document", "source-retained"),
                 Pointer("reconciliation-case", "case-retained"), new("report", "summary", "run", "report-wide")], Hash)
            { LedgerAmounts = [new(AmountId, Scope, "Securities", "AAPL", 400m, Hash)] },
            [new("manifest", GovernanceReportArtifactFormatDto.Json, "artifact.json", 512, Hash)], [])
        { Status = GovernanceReportPackStatusDto.Retained };
}
