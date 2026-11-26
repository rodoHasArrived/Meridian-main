using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Core.Serialization;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Security Master architecture review 2026-11-24, item F1. A bulk import restamps identifier
/// validity with the ingest time, so a file cannot backdate an identifier; a historical identifier
/// window enters the golden record only as a governed proposal (evidence + justification) that an
/// independent second operator approves through the workbench maker-checker gate, and publish
/// applies it with the proposed ValidFrom.
///
/// <para>The canonical write path is real: the security is created by
/// <see cref="SecurityMasterImportService"/> and the window is published through
/// <see cref="SecurityMasterService.AmendApprovedRevisionAsync"/> over a projection store whose
/// identifier lookup applies the same as-of window predicate as the PostgreSQL store
/// (<c>valid_from &lt;= as_of and (valid_to is null or valid_to &gt; as_of)</c>).</para>
/// </summary>
public sealed class HistoricalIdentifierWindowGovernanceTests
{
    private static readonly Guid SecurityId = Guid.Parse("2a1d7c4e-5b8f-4c3a-9e61-0f7b3d2c1a90");
    private static readonly DateTimeOffset FbFrom = new(2012, 5, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FbTo = new(2022, 6, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BacktestDate = new(2015, 1, 2);

    private const string Proposer = "ops.analyst";
    private const string Reviewer = "ops.reviewer";

    [Fact]
    public async Task ApprovedHistoricalWindow_ResolvesATickerBeforeTheIngestDate()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));

        // Before the window: the imported record only knows META from the ingest time.
        (await harness.Resolver.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "FB", null, AsOf(BacktestDate))))
            .Should().BeNull("an imported identifier is valid only from the ingest time");
        (await harness.Timeline.ResolveTickerForDateAsync("META", BacktestDate)).Should().Be("META");

        var proposed = await harness.Workbench.ProposeHistoricalIdentifierWindowAsync(FbWindow(harness.CurrentVersion));
        proposed.State.Should().Be(SecurityMasterRevisionStateDto.Draft);

        await harness.SubmitAsync(proposed.RevisionId, submitter: Proposer, reviewer: Reviewer);
        await harness.ApproveAsync(proposed.RevisionId, approver: Reviewer);
        var published = await harness.PublishAsync(proposed.RevisionId);

        published.CanonicalMerge!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.Merged);
        (await harness.Revisions.GetAsync(proposed.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);

        var fb = harness.Current.Identifiers.Should().ContainSingle(i => i.Value == "FB").Subject;
        fb.ValidFrom.Should().Be(FbFrom, "the governed amendment keeps the proposed ValidFrom");
        fb.ValidTo.Should().Be(FbTo);
        fb.IsPrimary.Should().BeFalse();
        harness.Current.Identifiers.Single(i => i.Value == "META").IsPrimary.Should().BeTrue();

        // Canonical resolution before the ingest date now succeeds, and backfill resolves the era ticker.
        (await harness.Resolver.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "FB", null, AsOf(BacktestDate))))
            .Should().Be(SecurityId);
        (await harness.Timeline.ResolveTickerForDateAsync("META", BacktestDate)).Should().Be("FB");
        (await harness.Resolver.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "FB", null, DateTimeOffset.UtcNow)))
            .Should().BeNull("the window closed when the ticker changed");

        harness.Appended.Last().Actor.Should().Be("ops.publisher");
    }

    [Fact]
    public async Task UnapprovedFile_CannotBackdateAnIdentifier()
    {
        var harness = new Harness();

        // The file claims FB was valid from 2012 to 2022 and META from 2012 — forged history.
        var importedAt = DateTimeOffset.UtcNow;
        var forged = CreateMeta(identifierValidFrom: FbFrom) with
        {
            Identifiers =
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "META", true, FbFrom),
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "FB", false, FbFrom, FbTo),
            ],
            EffectiveFrom = FbFrom,
        };
        await harness.ImportAsync(forged);

        harness.Current.Identifiers.Should().OnlyContain(i => i.ValidFrom >= importedAt && i.ValidTo == null,
            "an ungoverned file is no authority for identifier valid time");
        harness.Current.EffectiveFrom.Should().BeOnOrAfter(importedAt);
        (await harness.Resolver.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "FB", null, AsOf(BacktestDate))))
            .Should().BeNull();
        (await harness.Resolver.ResolveAsync(new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, "META", null, AsOf(BacktestDate))))
            .Should().BeNull();
        (await harness.Timeline.ResolveTickerForDateAsync("META", BacktestDate)).Should().Be("META");
    }

    [Theory]
    [InlineData(SecurityMasterRevisionStateDto.Draft)]
    [InlineData(SecurityMasterRevisionStateDto.Submitted)]
    public async Task UnapprovedProposal_CannotPublishOrBackdate(SecurityMasterRevisionStateDto stopAt)
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));
        var proposed = await harness.Workbench.ProposeHistoricalIdentifierWindowAsync(FbWindow(harness.CurrentVersion));
        if (stopAt == SecurityMasterRevisionStateDto.Submitted)
        {
            await harness.SubmitAsync(proposed.RevisionId, submitter: Proposer, reviewer: Reviewer);
        }

        await harness.Invoking(h => h.PublishAsync(proposed.RevisionId))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();

        // Nor can the window be pushed through the publication amender directly while unapproved.
        var direct = new AmendSecurityTermsRequest(
            SecurityId, harness.Current.Version, null, null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "FB", false, FbFrom, FbTo)], [],
            DateTimeOffset.UtcNow, SecurityMasterWriteAuthority.WorkbenchSourceSystem, Proposer,
            proposed.RevisionId.ToString("D"), "bypass");
        await harness.Canonical.Service.Invoking(s => s.AmendApprovedRevisionAsync(direct))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();

        harness.Current.Identifiers.Should().NotContain(i => i.Value == "FB");
        (await harness.Timeline.ResolveTickerForDateAsync("META", BacktestDate)).Should().Be("META");
    }

    [Fact]
    public async Task Proposer_CannotReviewOrApproveTheirOwnWindow_EvenWithTheIndependenceSettingOff()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));
        var proposed = await harness.Workbench.ProposeHistoricalIdentifierWindowAsync(FbWindow(harness.CurrentVersion));

        // The drafter cannot be the assigned reviewer (another operator submitting on their behalf).
        await harness.Invoking(h => h.SubmitAsync(proposed.RevisionId, submitter: "ops.colleague", reviewer: Proposer))
            .Should().ThrowAsync<ArgumentException>();

        // A colleague submits naming the proposer's peer; the proposer still cannot approve it.
        await harness.SubmitAsync(proposed.RevisionId, submitter: "ops.colleague", reviewer: Reviewer);
        await harness.Invoking(h => h.ApproveAsync(proposed.RevisionId, approver: Proposer))
            .Should().ThrowAsync<ArgumentException>();
        (await harness.Revisions.GetAsync(proposed.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Theory]
    [InlineData("", "Corporate action notice")]
    [InlineData("evidence-vault://sm/fb-rename-2022", " ")]
    public async Task Proposal_WithoutEvidenceOrJustification_IsRefused(string evidence, string justification)
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));

        await harness.Workbench
            .Invoking(s => s.ProposeHistoricalIdentifierWindowAsync(
                FbWindow(harness.CurrentVersion) with { EvidenceReference = evidence, Justification = justification }))
            .Should().ThrowAsync<ArgumentException>();
        (await harness.Revisions.ListBySecurityAsync(SecurityId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Proposal_ThatDoesNotStartInThePast_IsRefused()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));

        await harness.Workbench
            .Invoking(s => s.ProposeHistoricalIdentifierWindowAsync(
                FbWindow(harness.CurrentVersion) with { ValidFrom = DateTimeOffset.UtcNow.AddDays(1), ValidTo = null }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FieldEditRoute_RefusesTheReservedWindowPath()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));

        await harness.Workbench
            .Invoking(s => s.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
                SecurityId, harness.CurrentVersion, " Identifiers.HistoricalWindow ", "{}",
                DateTimeOffset.UtcNow, Proposer, "Sneak a window past the proposal checks.")))
            .Should().ThrowAsync<ArgumentException>();
        (await harness.Revisions.ListBySecurityAsync(SecurityId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DiscardingAProposal_RejectsItWithoutTouchingTheOverlay()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));
        var proposed = await harness.Workbench.ProposeHistoricalIdentifierWindowAsync(FbWindow(harness.CurrentVersion));

        await harness.Workbench.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(SecurityId, proposed.RevisionId, Proposer));

        (await harness.Revisions.GetAsync(proposed.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        harness.Overrides.Verify(
            o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task RetriedWindowPublication_IsIdempotent()
    {
        var harness = new Harness();
        await harness.ImportAsync(CreateMeta(identifierValidFrom: FbFrom));
        var proposed = await harness.Workbench.ProposeHistoricalIdentifierWindowAsync(FbWindow(harness.CurrentVersion));
        await harness.SubmitAsync(proposed.RevisionId, submitter: Proposer, reviewer: Reviewer);
        await harness.ApproveAsync(proposed.RevisionId, approver: Reviewer);
        await harness.PublishAsync(proposed.RevisionId);
        var appendedAfterPublish = harness.Appended.Count;

        // Re-running the handler (a retried fan-out) finds the window already canonical.
        await harness.WindowHandler.HandleAsync(PublishedEvent(proposed.RevisionId));

        harness.WindowHandler.TakeOutcome(proposed.RevisionId)!.Outcome
            .Should().Be(SecurityMasterCanonicalMergeOutcomeKind.AlreadyCanonical);
        harness.Appended.Should().HaveCount(appendedAfterPublish);
        harness.Current.Identifiers.Count(i => i.Value == "FB").Should().Be(1);
    }

    private static ProposeHistoricalIdentifierWindowRequest FbWindow(long expectedVersion)
        => new(
            SecurityId: SecurityId,
            ExpectedVersion: expectedVersion,
            Kind: SecurityIdentifierKind.Ticker,
            Value: "FB",
            Provider: null,
            ValidFrom: FbFrom,
            ValidTo: FbTo,
            EvidenceReference: "evidence-vault://security-master/meta-ticker-change-2022-06-09",
            Justification: "Listed as FB from the 2012 IPO until the 2022-06-09 ticker change to META.",
            Actor: Proposer);

    private static CreateSecurityRequest CreateMeta(DateTimeOffset identifierValidFrom)
        => new(
            SecurityId,
            "Equity",
            JsonSerializer.SerializeToElement(new { displayName = "Meta Platforms", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "META", true, identifierValidFrom)],
            identifierValidFrom,
            "vendor-file",
            "someone-else",
            "row-1",
            "bulk import");

    private static DateTimeOffset AsOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static SecurityMasterRevisionPublishedEvent PublishedEvent(Guid revisionId)
        => new(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Version: 0,
            EffectiveFrom: FbFrom,
            ChangedFields: [SecurityMasterHistoricalIdentifierWindow.FieldPath],
            DownstreamImpact: new SecurityMasterDownstreamImpactDto(
                FundProfileId: null, IsScoped: false, Severity: SecurityMasterImpactSeverity.None, Summary: "n/a",
                PortfolioExposureSummary: string.Empty, LedgerExposureSummary: string.Empty,
                ReconciliationExposureSummary: string.Empty, ReportPackExposureSummary: string.Empty,
                MatchedRunCount: 0, PortfolioExposureCount: 0, LedgerExposureCount: 0,
                ReconciliationExposureCount: 0, ReportPackExposureCount: 0, Links: []),
            AffectedLedgerBookIds: [],
            Actor: "ops.publisher",
            CorrelationId: null);

    private sealed class Harness
    {
        private readonly Mock<ISecurityMasterStore> _store = new(MockBehavior.Loose);
        private readonly Mock<IOperationsContinuityWorkflowService> _workflow = new(MockBehavior.Loose);
        private readonly Guid _workflowId = Guid.NewGuid();

        public Harness()
        {
            // Governance on: operator-direct amendments are refused, so publication is the only way in.
            var options = new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = true });
            Canonical = new CanonicalAmendHarness(
                _store,
                governedTermAmendmentPolicy: new GovernedTermAmendmentPolicy(GovernanceRequirementsFor.NonProduction(options)),
                revisionStore: Revisions);

            _store.Setup(s => s.GetProjectionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Canonical.Upserted.LastOrDefault(p => p.SecurityId == id));
            _store.Setup(s => s.GetByIdentifierAsync(
                    It.IsAny<SecurityIdentifierKind>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOf, bool _, CancellationToken _) =>
                    FindByIdentifier(kind, value, provider, asOf));

            var workbenchEvents = new Mock<ISecurityMasterEventStore>(MockBehavior.Loose);
            workbenchEvents.Setup(e => e.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Canonical.Upserted.Count == 0
                    ? Array.Empty<SecurityMasterEventEnvelope>()
                    : [new SecurityMasterEventEnvelope(CurrentVersion, SecurityId, CurrentVersion, "seed",
                        DateTimeOffset.UnixEpoch, "seed", null, null, default, default)]);

            _workflow.Setup(w => w.SubmitForApprovalAsync(_workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OperationsTransitionResultDto(true, null, null, null, [], [], 1));
            _workflow.Setup(w => w.ApproveWorkflowAsync(_workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OperationsTransitionResultDto(true, null, null, null, [], [], 2));

            var restatement = new Mock<IPeriodAwareRestatementResolver>();
            restatement.Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []));
            var affectedBooks = new Mock<IAffectedLedgerBookResolver>();
            affectedBooks.Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<Guid>)[]);

            WindowHandler = new ApprovedHistoricalIdentifierWindowHandler(
                Revisions, NullLogger<ApprovedHistoricalIdentifierWindowHandler>.Instance, _store.Object, Canonical.Service);
            var mergeHandler = new ApprovedFieldEditCanonicalMergeHandler(
                Revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, _store.Object, Canonical.Service);

            Workbench = new SecurityMasterWorkbenchCommandService(
                workbenchEvents.Object,
                Overrides.Object,
                Mock.Of<ISecurityMasterConflictAuthorityPolicy>(),
                Mock.Of<ISecurityMasterConflictService>(),
                Mock.Of<ISecurityMasterWorkbenchQueryService>(),
                _workflow.Object,
                Revisions,
                restatement.Object,
                affectedBooks.Object,
                [mergeHandler, WindowHandler],
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance,
                projectionStore: _store.Object);

            var resolver = new SecurityResolver(_store.Object);
            Resolver = resolver;
            Timeline = new SecurityMasterHistoricalSymbolTimelineResolver(
                resolver, _store.Object, NullLogger<SecurityMasterHistoricalSymbolTimelineResolver>.Instance);
        }

        public InMemorySecurityMasterRevisionStore Revisions { get; } = new();

        public Mock<IOperatorOverridesStore> Overrides { get; } = new(MockBehavior.Loose);

        public CanonicalAmendHarness Canonical { get; }

        public ApprovedHistoricalIdentifierWindowHandler WindowHandler { get; }

        public SecurityMasterWorkbenchCommandService Workbench { get; }

        public ISecurityResolver Resolver { get; }

        public SecurityMasterHistoricalSymbolTimelineResolver Timeline { get; }

        public SecurityProjectionRecord Current => Canonical.Upserted.Last(p => p.SecurityId == SecurityId);

        public long CurrentVersion => Current.Version;

        public List<SecurityMasterEventEnvelope> Appended => Canonical.Appended;

        public async Task ImportAsync(CreateSecurityRequest request)
        {
            var import = new SecurityMasterImportService(
                Canonical.Service, new SecurityMasterCsvParser(), NullLogger<SecurityMasterImportService>.Instance);
            var file = JsonSerializer.Serialize(
                new List<CreateSecurityRequest> { request }, SecurityMasterJsonContext.Default.ListCreateSecurityRequest);

            var result = await import.ImportAsync(file, ".json", "ops.importer");

            result.Imported.Should().Be(1, string.Join("; ", result.Errors));
        }

        public Task SubmitAsync(Guid revisionId, string submitter, string reviewer)
            => Workbench.SubmitForApprovalAsync(new SubmitSecurityMasterRevisionRequest(
                SecurityId, revisionId, submitter, "Historical window for review.",
                WorkflowId: _workflowId, ExpectedWorkflowVersion: 0, Reviewer: reviewer));

        public Task ApproveAsync(Guid revisionId, string approver)
            => Workbench.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
                SecurityId, revisionId, _workflowId, 1, approver, approver,
                "Evidence matches the exchange ticker-change notice.", "rp-sm"));

        public Task<SecurityMasterPublishResultDto> PublishAsync(Guid revisionId)
            => Workbench.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
                SecurityId, revisionId, "ops.publisher", Reviewer));

        private SecurityProjectionRecord? FindByIdentifier(
            SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOf)
        {
            var normalizedValue = SecurityIdentifierNormalizer.NormalizeValue(kind, value);
            var normalizedProvider = SecurityIdentifierNormalizer.NormalizeProvider(provider);
            return Canonical.Upserted
                .GroupBy(static p => p.SecurityId)
                .Select(static g => g.Last())
                .FirstOrDefault(p => p.Identifiers.Any(i =>
                    i.Kind == kind
                    && SecurityIdentifierNormalizer.NormalizeValue(i.Kind, i.Value) == normalizedValue
                    && SecurityIdentifierNormalizer.NormalizeProvider(i.Provider) == normalizedProvider
                    && i.ValidFrom <= asOf
                    && (i.ValidTo is null || i.ValidTo > asOf)));
        }
    }
}
