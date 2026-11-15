using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityReportInputAttributionTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task ExactRetainedVersion_ResolvesItsOwnMakerAndDecision_WithoutUsingLatest(long version, bool preFormatHistory)
    {
        var fixture = new Fixture(version, preFormatHistory);
        var retainedBytes = fixture.Retained.AssetSpecificTerms.GetRawText();
        var result = await fixture.ResolveAsync();
        result.SecurityVersion.Should().Be(version);
        result.EventActor.Should().Be("actual-publisher");
        result.DraftedBy.Should().Be("retained-maker");
        result.SubmittedBy.Should().Be("retained-submitter");
        result.ApprovedBy.Should().Be("actual-checker");
        result.ApprovalId.Should().Be("actual-decision");
        result.ApprovedAt.Should().Be(Fixture.DecidedAt);
        result.EvidenceRoute.Should().Contain(fixture.Workflow.WorkflowId.ToString("D"));
        result.Gaps.Should().BeEmpty();
        fixture.Retained.AssetSpecificTerms.GetRawText().Should().Be(retainedBytes);
        SecurityTermsEnvelope.GetDeclaredFormat(fixture.Retained.AssetSpecificTerms).Should()
            .Be(preFormatHistory ? null : SecurityTermsFormats.Flat);
        await fixture.Revisions.DidNotReceive().GetAsync(fixture.LatestRevisionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyRevisionWithoutDrafter_DoesNotAttributeTheEventActorOrPublisherAsMaker()
    {
        var fixture = new Fixture();
        fixture.Revision = fixture.Revision with { DraftedBy = null, SubmittedBy = null };
        var result = await fixture.ResolveAsync();
        result.EventActor.Should().Be("actual-publisher");
        result.DraftedBy.Should().BeNull();
        result.SubmittedBy.Should().Be("retained-submitter");
        result.ApprovedBy.Should().Be("actual-checker");
        result.Gaps.Should().Contain(reason => reason.Contains("did not retain its drafter"));
    }

    [Fact]
    public async Task ForeignWorkflowOwnership_HidesAllRevisionAndDecisionIdentities()
    {
        var fixture = new Fixture();
        fixture.Ownership = new("fund-report", "foreign-tenant", "foreign-company");
        var result = await fixture.ResolveAsync();
        result.EventActor.Should().Be("actual-publisher");
        result.RevisionId.Should().BeNull();
        result.DraftedBy.Should().BeNull();
        result.SubmittedBy.Should().BeNull();
        result.ApprovedBy.Should().BeNull();
        result.EvidenceRoute.Should().BeNull();
        result.Gaps.Should().Contain(reason => reason.Contains("ownership cannot be verified"));
    }

    [Fact]
    public async Task AnotherRevisionSubmissionMarker_CannotSupplyApprovalAttribution()
    {
        var fixture = new Fixture();
        fixture.Workflow = fixture.Workflow with
        {
            Approvals = fixture.Workflow.Approvals.Select(approval =>
            approval.Status == OperationsApprovalStateDto.Submitted ? approval with { Rationale = "unrelated revision" } : approval).ToArray()
        };
        var result = await fixture.ResolveAsync();
        result.ApprovedBy.Should().BeNull();
        result.RevisionId.Should().BeNull();
        result.Gaps.Should().Contain(reason => reason.Contains("exact revision submission"));
    }

    [Fact]
    public async Task MissingExactEvent_NeverFallsBackToLatestVersion()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResolveAsync(fixture.Retained with { Version = 42 }, "tenant-report", "company-report");
        result.EventActor.Should().BeNull();
        result.ApprovedBy.Should().BeNull();
        result.Gaps.Should().Contain(reason => reason.Contains("no latest-version fallback"));
        await fixture.Revisions.DidNotReceiveWithAnyArgs().GetAsync(default);
    }

    [Fact]
    public async Task SameVersionWithContradictoryReceiptTerms_RefusesAttribution()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResolveAsync(fixture.Retained with { AssetSpecificTerms = JsonSerializer.SerializeToElement(new { baseCurrency = "GBP", quoteCurrency = "USD" }) },
            "tenant-report", "company-report");
        result.EventActor.Should().BeNull();
        result.Gaps.Should().Contain(reason => reason.Contains("does not match"));
    }

    private sealed class Fixture
    {
        internal static readonly DateTimeOffset DecidedAt = DateTimeOffset.Parse("2026-08-02T12:00:00Z");
        internal readonly ISecurityMasterRevisionStore Revisions = Substitute.For<ISecurityMasterRevisionStore>();
        internal readonly SecurityReportInputAttributionResolver Service;
        internal readonly SecurityDetailDto Retained;
        internal readonly Guid LatestRevisionId = Guid.NewGuid();
        internal SecurityMasterRevisionRecord Revision;
        internal OperationsContinuityWorkflowDto Workflow;
        internal FundProfileOwnership Ownership = new("fund-report", "tenant-report", "company-report");

        internal Fixture(long version = 2, bool preFormatHistory = false)
        {
            var revisionId = Guid.NewGuid();
            var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("FxSpot",
                JsonSerializer.SerializeToElement(new { baseCurrency = "EUR", quoteCurrency = "USD" })).WithDeclaredTermsFormat() with
            {
                Version = version,
                Provenance = JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = SecurityMasterWriteAuthority.WorkbenchSourceSystem,
                    sourceRecordId = revisionId.ToString("D"),
                    asOf = "2026-08-03T12:00:00+00:00",
                    updatedBy = "actual-publisher"
                })
            };
            var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
            Retained = SecurityMasterMapping.ToDetail(SecurityEconomicDefinitionAdapter.ToProjection(economic));
            if (preFormatHistory)
            {
                economic = economic with { LegacyAssetSpecificTerms = SecurityTermsEnvelope.WithoutRedundantDeclaredFormat(economic.LegacyAssetSpecificTerms!.Value) };
                Retained = Retained with { AssetSpecificTerms = SecurityTermsEnvelope.WithoutRedundantDeclaredFormat(Retained.AssetSpecificTerms) };
            }
            var exact = SecurityMasterMapping.ToEventEnvelope(economic, "SecurityTermsAmended", "actual-publisher", SecurityMasterWriteAuthority.WorkbenchSourceSystem, null, version)
                with
            { EventTimestamp = DecidedAt.AddDays(1) };
            var latestEconomic = economic with
            {
                Version = version + 1,
                Provenance = JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = SecurityMasterWriteAuthority.WorkbenchSourceSystem,
                    sourceRecordId = LatestRevisionId.ToString("D"),
                    asOf = "2026-08-04T12:00:00+00:00",
                    updatedBy = "newer-publisher"
                })
            };
            var latest = SecurityMasterMapping.ToEventEnvelope(latestEconomic, "SecurityTermsAmended", "newer-publisher", SecurityMasterWriteAuthority.WorkbenchSourceSystem, null, version + 1);
            var events = Substitute.For<ISecurityMasterEventStore>();
            events.LoadAsync(Retained.SecurityId, Arg.Any<CancellationToken>()).Returns([exact, latest]);
            var workflowId = Guid.NewGuid();
            Revision = new(revisionId, Retained.SecurityId, SecurityMasterRevisionStateDto.Published, "revision-latest-actor",
                DecidedAt.AddDays(-2), DecidedAt.AddDays(1), workflowId, FundProfileId: "fund-report")
            { DraftedBy = "retained-maker", SubmittedBy = "retained-submitter" };
            Revisions.GetAsync(revisionId, Arg.Any<CancellationToken>()).Returns(_ => Revision);
            var approvals = new OperationsApprovalDto[]
            {
                new("submission", OperationsApprovalStateDto.Submitted, "retained-submitter", "actual-checker",
                    $"Reviewed evidence [security-master-revision:{revisionId:D}]", DecidedAt.AddHours(-1), null, []),
                new("actual-decision", OperationsApprovalStateDto.Approved, "retained-submitter", "actual-checker",
                    "Actual retained approval decision", DecidedAt.AddHours(-1), DecidedAt, [])
            };
            var bookId = Guid.NewGuid();
            Workflow = new(workflowId, Guid.NewGuid(), "2026-08", null, "Synthetic", DecidedAt.AddDays(-2), DecidedAt, 5,
                default, default, default, default, default, OperationsApprovalStateDto.Approved,
                [], [], [], null, approvals, null!, [], [], [], [], LedgerBookId: bookId);
            var workflows = Substitute.For<IOperationsContinuityWorkflowService>();
            workflows.GetAsync(workflowId, Arg.Any<CancellationToken>()).Returns(_ => Workflow);
            var ledger = Substitute.For<ILedgerJournalStore>();
            ledger.GetLedgerBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(bookId, "fund-report", Guid.NewGuid(),
                default, "Synthetic book", "USD", DecidedAt.AddDays(-4), DecidedAt));
            var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
            tenancy.ResolveAsync("fund-report", Arg.Any<CancellationToken>()).Returns(_ => Ownership);
            Service = new(events, Revisions, workflows, ledger, tenancy);
        }

        internal Task<SecurityReportChangeAttributionDto> ResolveAsync()
            => Service.ResolveAsync(Retained, "tenant-report", "company-report");
    }
}
