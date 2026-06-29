using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Report-pack-backed restatement candidate location: a closed-period reference-data edit surfaces the
/// published packs that consumed the edited security (from retained report-line provenance) as governed
/// restatement candidates, rather than degrading to a manual locate-affected-packs task.
/// </summary>
public sealed class ReportPackRestatementCandidateResolverTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSecurityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LedgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string FundProfileId = "fund-alpha";
    private static readonly DateOnly EffectiveDate = new(2026, 03, 15);

    [Fact]
    public async Task NoFundScope_ReturnsNoCandidates()
    {
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03", SecurityLine("line-1", SecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], fundProfileId: null);

        result.Candidates.Should().BeEmpty("without a fund scope the published packs cannot be narrowed");
        result.HasNonActionableMatches.Should().BeFalse();
    }

    [Fact]
    public async Task PublishedPackReferencingSecurity_BecomesCandidate()
    {
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03", SecurityLine("nav.line", SecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().ContainSingle();
        var candidate = result.Candidates[0];
        candidate.PeriodLabel.Should().Be("2026-P03");
        candidate.ChangedLines.Should().ContainSingle().Which.LineKey.Should().Be("nav.line");
        result.HasNonActionableMatches.Should().BeFalse();
    }

    [Fact]
    public async Task PackForOtherSecurity_IsNotACandidate()
    {
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03", SecurityLine("line-1", OtherSecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().BeEmpty("the pack's provenance ties its line to a different security");
    }

    [Fact]
    public async Task PackForOtherFund_IsNotACandidate()
    {
        var resolver = NewResolver(
            PublishedPack("fund-beta", "2026-P03", SecurityLine("line-1", SecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().BeEmpty("only the impacted fund's packs are in scope");
    }

    [Fact]
    public async Task UnpublishedPack_IsNotACandidate()
    {
        var resolver = NewResolver(
            PackInState(ReportPackWorkflowStateDto.Approved, FundProfileId, "2026-P03", SecurityLine("line-1", SecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().BeEmpty("only a live published output can require a restatement");
        result.HasNonActionableMatches.Should().BeFalse("an approved (not yet published) pack is not a live output to flag");
    }

    [Fact]
    public async Task RestatedPackReferencingSecurity_IsNotAnActionableCandidate()
    {
        // An already-Restated pack cannot be re-restated (workflow allows Restated -> Archived only), so
        // surfacing it as a candidate would hand the operator an action Restate() rejects. It is reported
        // as a non-actionable match (logged, and a manual-follow-up signal to the caller) rather than a
        // candidate or a silent drop.
        var resolver = NewResolver(
            PackInState(ReportPackWorkflowStateDto.Restated, FundProfileId, "2026-P02", SecurityLine("line-1", SecurityId)));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().BeEmpty("a Restated pack is not actionable through Restate() and must not be a candidate");
        result.HasNonActionableMatches.Should().BeTrue("the affected Restated pack must surface a manual-follow-up signal");
    }

    [Fact]
    public async Task PublishedPackWithoutMatchingProvenance_IsNotACandidate()
    {
        // Precise matching: a published pack with provenance that does not reference the security is
        // left to the caller's default-deny safety net rather than surfaced as a false candidate.
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03",
                new ReportPackLineProvenanceDto(
                    LineKey: "cash.line",
                    SourceKind: "ledger",
                    SourceId: "ledger-entry-9",
                    EvidenceId: "ev-9")));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().BeEmpty();
        result.HasNonActionableMatches.Should().BeFalse();
    }

    [Fact]
    public async Task SecurityKindSource_MatchesBySourceId()
    {
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03",
                new ReportPackLineProvenanceDto(
                    LineKey: "holding.line",
                    SourceKind: "security-master",
                    SourceId: SecurityId.ToString("D"),
                    EvidenceId: "ev-1")));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        result.Candidates.Should().ContainSingle();
    }

    [Fact]
    public async Task MatchedLine_CarriesProvenanceEvidenceForward()
    {
        var resolver = NewResolver(
            PublishedPack(FundProfileId, "2026-P03", SecurityLine("nav.line", SecurityId, evidenceId: "ev-42")));

        var result = await resolver.ResolveAsync(
            SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        var changedLine = result.Candidates.Should().ContainSingle().Subject.ChangedLines.Should().ContainSingle().Subject;
        changedLine.EvidenceLinks.Should().ContainSingle().Which.EvidenceId.Should().Be("ev-42");
    }

    [Fact]
    public async Task IndexBackedAndScan_ProduceIdenticalResults()
    {
        // The index-backed lookup must be a behaviour-preserving optimization of the full fund scan.
        var seed = new[]
        {
            PublishedPack(FundProfileId, "2026-P03", SecurityLine("nav.line", SecurityId), SecurityLine("alt.line", OtherSecurityId)),
            PackInState(ReportPackWorkflowStateDto.Restated, FundProfileId, "2026-P02", SecurityLine("old.line", SecurityId)),
            PublishedPack("fund-beta", "2026-P03", SecurityLine("other-fund.line", SecurityId)),
            PackInState(ReportPackWorkflowStateDto.Approved, FundProfileId, "2026-P03", SecurityLine("draft.line", SecurityId)),
        };

        var indexed = await NewResolver(useIndex: true, seed)
            .ResolveAsync(SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);
        var scanned = await NewResolver(useIndex: false, seed)
            .ResolveAsync(SecurityId, LedgerBookId, EffectiveDate, ["EconomicDefinition.Coupon"], FundProfileId);

        indexed.Candidates.Select(c => c.ReportId).Should().BeEquivalentTo(scanned.Candidates.Select(c => c.ReportId));
        indexed.HasNonActionableMatches.Should().Be(scanned.HasNonActionableMatches);
        indexed.Candidates.Should().ContainSingle("only the Published same-fund pack is actionable");
        indexed.HasNonActionableMatches.Should().BeTrue("the Restated same-fund pack is a non-actionable match");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ReportPackRestatementCandidateResolver NewResolver(params ReportPackWorkflowRecordDto[] seedRecords)
        => NewResolver(useIndex: true, seedRecords);

    private static ReportPackRestatementCandidateResolver NewResolver(bool useIndex, params ReportPackWorkflowRecordDto[] seedRecords)
    {
        IReportPackSecurityLineIndex? index = useIndex ? new InMemoryReportPackSecurityLineIndex() : null;
        var workflow = new ReportPackWorkflowService(new SeedStore(seedRecords), index);
        return new ReportPackRestatementCandidateResolver(
            workflow, NullLogger<ReportPackRestatementCandidateResolver>.Instance);
    }

    private static ReportPackLineProvenanceDto SecurityLine(string lineKey, Guid securityId, string evidenceId = "ev-1")
        => new(
            LineKey: lineKey,
            SourceKind: "security-master",
            SourceId: securityId.ToString("D"),
            EvidenceId: evidenceId,
            SecurityMasterId: securityId.ToString("D"));

    private static ReportPackWorkflowRecordDto PublishedPack(
        string fundProfileId, string period, params ReportPackLineProvenanceDto[] lineProvenance)
        => PackInState(ReportPackWorkflowStateDto.Published, fundProfileId, period, lineProvenance);

    private static ReportPackWorkflowRecordDto PackInState(
        ReportPackWorkflowStateDto state, string fundProfileId, string period, params ReportPackLineProvenanceDto[] lineProvenance)
    {
        var now = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        return new ReportPackWorkflowRecordDto(
            ReportId: Guid.NewGuid(),
            FundProfileId: fundProfileId,
            FundAccountId: $"{fundProfileId}-acct",
            Period: period,
            TemplateId: new VersionedReportTemplateIdDto("nav-pack", 1),
            State: state,
            Version: 1,
            CreatedAt: now,
            CreatedBy: "ops.analyst",
            UpdatedAt: now,
            AuditTrail: [new ReportPackAuditEventDto(now, "ops.analyst", "publish", ReportPackWorkflowStateDto.Approved, state)],
            Restatement: null,
            LineProvenance: lineProvenance);
    }

    private sealed class SeedStore : IReportPackWorkflowRecordStore
    {
        private readonly IReadOnlyList<ReportPackWorkflowRecordDto> _records;
        public SeedStore(IReadOnlyList<ReportPackWorkflowRecordDto> records) => _records = records;

        public IReadOnlyList<ReportPackWorkflowRecordDto> Load() => _records;
        public void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records) { }
    }
}
