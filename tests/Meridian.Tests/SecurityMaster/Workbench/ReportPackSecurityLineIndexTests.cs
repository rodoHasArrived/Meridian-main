using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Unit coverage for the derived security→report-line index and the shared matcher it relies on. The
/// index must stay an exact reverse of the matcher (so it agrees with the candidate resolver) and must
/// stay consistent under re-upsert, removal, and rebuild.
/// </summary>
public sealed class ReportPackSecurityLineIndexTests
{
    private static readonly Guid SecurityA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecurityB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string FundA = "fund-alpha";
    private const string FundB = "fund-beta";

    [Fact]
    public void Upsert_ThenLookupByFund_ReturnsEntryWithLineKeys()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        var record = Pack(FundA, ReportPackWorkflowStateDto.Published,
            SecurityLine("nav.line", SecurityA), SecurityLine("income.line", SecurityA));

        index.Upsert(record);

        var entry = index.LookupByFund(SecurityA, FundA).Should().ContainSingle().Subject;
        entry.ReportId.Should().Be(record.ReportId);
        entry.FundProfileId.Should().Be(FundA);
        entry.State.Should().Be(ReportPackWorkflowStateDto.Published);
        entry.LineKeys.Should().BeEquivalentTo(["nav.line", "income.line"]);
    }

    [Fact]
    public void LookupByFund_WrongFund_ReturnsEmpty()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        index.Upsert(Pack(FundA, ReportPackWorkflowStateDto.Published, SecurityLine("nav.line", SecurityA)));

        index.LookupByFund(SecurityA, FundB).Should().BeEmpty();
    }

    [Fact]
    public void Lookup_ReturnsEntriesAcrossFunds()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        index.Upsert(Pack(FundA, ReportPackWorkflowStateDto.Published, SecurityLine("a.line", SecurityA)));
        index.Upsert(Pack(FundB, ReportPackWorkflowStateDto.Published, SecurityLine("b.line", SecurityA)));

        index.Lookup(SecurityA).Should().HaveCount(2);
        index.Lookup(SecurityA).Select(e => e.FundProfileId).Should().BeEquivalentTo([FundA, FundB]);
    }

    [Fact]
    public void Upsert_SameReport_RefreshesStateInPlace()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        var published = Pack(FundA, ReportPackWorkflowStateDto.Published, SecurityLine("nav.line", SecurityA));
        index.Upsert(published);

        var restated = published with { State = ReportPackWorkflowStateDto.Restated };
        index.Upsert(restated);

        var entry = index.LookupByFund(SecurityA, FundA).Should().ContainSingle().Subject;
        entry.State.Should().Be(ReportPackWorkflowStateDto.Restated);
    }

    [Fact]
    public void Upsert_WhenSecurityNoLongerReferenced_RemovesStaleEntry()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        var withSecurity = Pack(FundA, ReportPackWorkflowStateDto.Published, SecurityLine("nav.line", SecurityA));
        index.Upsert(withSecurity);

        // Re-upsert the same report with provenance that no longer references SecurityA.
        var withoutSecurity = withSecurity with
        {
            LineProvenance = [new ReportPackLineProvenanceDto(LineKey: "cash.line", SourceKind: "ledger", SourceId: "ledger-1", EvidenceId: "ev-1")]
        };
        index.Upsert(withoutSecurity);

        index.Lookup(SecurityA).Should().BeEmpty();
    }

    [Fact]
    public void RemoveReport_RemovesAllItsEntries()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        var record = Pack(FundA, ReportPackWorkflowStateDto.Published,
            SecurityLine("a.line", SecurityA), SecurityLine("b.line", SecurityB));
        index.Upsert(record);

        index.RemoveReport(record.ReportId);

        index.Lookup(SecurityA).Should().BeEmpty();
        index.Lookup(SecurityB).Should().BeEmpty();
    }

    [Fact]
    public void Rebuild_ReplacesEntireIndex()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        index.Upsert(Pack(FundA, ReportPackWorkflowStateDto.Published, SecurityLine("stale.line", SecurityA)));

        var fresh = Pack(FundB, ReportPackWorkflowStateDto.Published, SecurityLine("fresh.line", SecurityB));
        index.Rebuild([fresh]);

        index.Lookup(SecurityA).Should().BeEmpty("the prior contents are cleared on rebuild");
        index.LookupByFund(SecurityB, FundB).Should().ContainSingle().Which.ReportId.Should().Be(fresh.ReportId);
    }

    [Fact]
    public void Upsert_LineReferencingTwoSecurities_IndexesUnderBoth()
    {
        var index = new InMemoryReportPackSecurityLineIndex();
        var line = new ReportPackLineProvenanceDto(
            LineKey: "blended.line",
            SourceKind: "security-master",
            SourceId: SecurityA.ToString("D"),
            EvidenceId: "ev-1",
            SecurityMasterId: SecurityA.ToString("D"),
            SecurityDefinitionId: SecurityB.ToString("D"));
        index.Upsert(Pack(FundA, ReportPackWorkflowStateDto.Published, line));

        index.Lookup(SecurityA).Should().ContainSingle();
        index.Lookup(SecurityB).Should().ContainSingle();
    }

    [Fact]
    public void ReferencedSecurityIds_IsExactReverseOf_LineReferencesSecurity()
    {
        var line = new ReportPackLineProvenanceDto(
            LineKey: "nav.line",
            SourceKind: "security-master",
            SourceId: SecurityA.ToString("D"),
            EvidenceId: "ev-1",
            SecurityMasterId: SecurityA.ToString("D"),
            SecurityDefinitionId: SecurityB.ToString("D"));

        var referenced = ReportPackSecurityLineMatcher.ReferencedSecurityIds(line).ToArray();

        referenced.Should().BeEquivalentTo([SecurityA, SecurityB]);
        foreach (var securityId in referenced)
        {
            ReportPackSecurityLineMatcher.LineReferencesSecurity(line, securityId).Should().BeTrue();
        }
        ReportPackSecurityLineMatcher.LineReferencesSecurity(line, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void NonSecurityKindSource_DoesNotReferenceSecurityBySourceId()
    {
        // A ledger-kind source whose id happens to be a guid must NOT be treated as a security reference.
        var line = new ReportPackLineProvenanceDto(
            LineKey: "ledger.line",
            SourceKind: "ledger",
            SourceId: SecurityA.ToString("D"),
            EvidenceId: "ev-1");

        ReportPackSecurityLineMatcher.ReferencedSecurityIds(line).Should().BeEmpty();
        ReportPackSecurityLineMatcher.LineReferencesSecurity(line, SecurityA).Should().BeFalse();
    }

    private static ReportPackLineProvenanceDto SecurityLine(string lineKey, Guid securityId)
        => new(
            LineKey: lineKey,
            SourceKind: "security-master",
            SourceId: securityId.ToString("D"),
            EvidenceId: "ev-1",
            SecurityMasterId: securityId.ToString("D"));

    private static ReportPackWorkflowRecordDto Pack(
        string fundProfileId, ReportPackWorkflowStateDto state, params ReportPackLineProvenanceDto[] lineProvenance)
    {
        var now = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        return new ReportPackWorkflowRecordDto(
            ReportId: Guid.NewGuid(),
            FundProfileId: fundProfileId,
            FundAccountId: $"{fundProfileId}-acct",
            Period: "2026-P03",
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
}
