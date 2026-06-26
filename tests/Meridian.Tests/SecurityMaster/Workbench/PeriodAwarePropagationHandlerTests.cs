// BLUEPRINT: tests for Phase 3 period-aware propagation of the Security Master Passport Workbench.
// See docs/plans/security-master-passport-workbench.md — Design Decision D3 (Open Question #2 RESOLVED).
//
// Q2 RESOLUTION encoded here: the AUTHORITATIVE period-lock source is the ledger accounting period
// status (LedgerAccountingPeriod.Status ∈ Open/SoftClosed/HardClosed, enum LedgerPeriodStatusDto),
// the same source LedgerPeriodPostingGuard.Validate(...) enforces at post-time — NOT
// FundAccountCloseReadinessService (readiness only). The handler routes by the D3 matrix:
//   Open        -> no-op (lower-Order handlers already propagated)
//   SoftClosed  -> governed Adjustment posting carrying approval metadata
//   HardClosed  -> restatement proposal, NO posting
//   indeterminate (period missing / unrecognized) -> default-deny: treat as HardClosed
//
// Intended RED state — blueprint types not implemented yet:
//   - PeriodAwarePropagationHandler : ISecurityMasterRevisionPublishedHandler
//   - ILedgerPeriodLockReader (returns LedgerPeriodStatusDto; HardClosed when indeterminate)
//   - IGovernedLedgerAdjustmentPoster (soft-closed adjustment posting seam)
//   - IReportPackRestatementProposer (implemented by ReportingWorkflowService.ProposeRestatement)
//   - SecurityMasterRevisionPublishedEvent (now carries AffectedLedgerBookIds)
//
// Real existing type used: LedgerPeriodStatusDto (Meridian.Contracts.Ledger).

using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class PeriodAwarePropagationHandlerTests
{
    private static readonly Guid SecurityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LedgerBookId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_OpenPeriod_NoProposalNoAdjustment()
    {
        var harness = new Harness(periodStatus: LedgerPeriodStatusDto.Open);

        await harness.Handler.HandleAsync(BuildEvent());

        harness.AdjustmentPoster.Posted.Should().BeEmpty("open periods propagate via lower-Order handlers");
        harness.RestatementProposer.Proposals.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SoftClosedPeriod_PostsGovernedAdjustmentWithApproval()
    {
        var harness = new Harness(periodStatus: LedgerPeriodStatusDto.SoftClosed);

        await harness.Handler.HandleAsync(BuildEvent());

        harness.AdjustmentPoster.Posted.Should().ContainSingle(
            "soft-closed periods accept approved Adjustment postings — the edit posts as a governed adjustment, not blocked");
        harness.RestatementProposer.Proposals.Should().BeEmpty(
            "no published report pack consumed the line in this case");
    }

    [Fact]
    public async Task Handle_HardClosedPeriod_ProposesRestatementNoPosting()
    {
        // THE headline invariant: a hard-closed book is never silently mutated.
        var harness = new Harness(periodStatus: LedgerPeriodStatusDto.HardClosed);

        await harness.Handler.HandleAsync(BuildEvent());

        harness.AdjustmentPoster.Posted.Should().BeEmpty("hard-closed periods permit no postings");
        harness.RestatementProposer.Proposals.Should().ContainSingle()
            .Which.ChangedLines.Should().NotBeEmpty("the proposal must carry changed-line evidence");
    }

    [Fact]
    public async Task Handle_IndeterminatePeriod_DefaultDenyTreatsAsHardClosed()
    {
        // Default-deny: when the covering period cannot be resolved, the authoritative reader returns
        // HardClosed, so the handler proposes a restatement rather than posting.
        var harness = new Harness(periodStatus: null /* unconfigured => reader returns HardClosed */);

        await harness.Handler.HandleAsync(BuildEvent());

        harness.AdjustmentPoster.Posted.Should().BeEmpty();
        harness.RestatementProposer.Proposals.Should().ContainSingle("indeterminate period state must default-deny to restatement");
    }

    private static SecurityMasterRevisionPublishedEvent BuildEvent()
        => new(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Version: 12,
            EffectiveFrom: EffectiveFrom,
            ChangedFields: new[] { "EconomicDefinition.Coupon" },
            DownstreamImpact: MinimalImpact(),
            AffectedLedgerBookIds: new[] { LedgerBookId },
            Actor: "ops.analyst",
            CorrelationId: null);

    private static SecurityMasterDownstreamImpactDto MinimalImpact()
        => new(
            FundProfileId: "fund-1",
            IsScoped: true,
            Severity: SecurityMasterImpactSeverity.Medium,
            Summary: "coupon correction",
            PortfolioExposureSummary: "",
            LedgerExposureSummary: "1 ledger book",
            ReconciliationExposureSummary: "",
            ReportPackExposureSummary: "1 published pack",
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 1,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 1,
            Links: Array.Empty<SecurityMasterImpactLinkDto>());

    // ---- Harness + hand-rolled spies ------------------------------------------------------------

    private sealed class Harness
    {
        public FakeLedgerPeriodLockReader LockReader { get; }
        public SpyGovernedAdjustmentPoster AdjustmentPoster { get; } = new();
        public SpyRestatementProposer RestatementProposer { get; } = new();
        public PeriodAwarePropagationHandler Handler { get; }

        public Harness(LedgerPeriodStatusDto? periodStatus)
        {
            LockReader = new FakeLedgerPeriodLockReader();
            if (periodStatus is { } status)
            {
                LockReader.Configure(LedgerBookId, EffectiveFrom, status);
            }

            // BLUEPRINT: assumed constructor (ILedgerPeriodLockReader, IGovernedLedgerAdjustmentPoster,
            // IReportPackRestatementProposer, ILogger). Logger omitted via NullLogger in the real impl.
            Handler = new PeriodAwarePropagationHandler(LockReader, AdjustmentPoster, RestatementProposer);
        }
    }

    /// <summary>Models the authoritative reader, including its default-deny posture: an unconfigured
    /// (book, date) resolves to HardClosed.</summary>
    private sealed class FakeLedgerPeriodLockReader : ILedgerPeriodLockReader
    {
        private readonly Dictionary<(Guid, DateOnly), LedgerPeriodStatusDto> _statuses = new();

        public void Configure(Guid ledgerBookId, DateTimeOffset date, LedgerPeriodStatusDto status)
            => _statuses[(ledgerBookId, DateOnly.FromDateTime(date.UtcDateTime))] = status;

        public Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(Guid ledgerBookId, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(_statuses.TryGetValue((ledgerBookId, date), out var status)
                ? status
                : LedgerPeriodStatusDto.HardClosed); // default-deny
    }

    private sealed class SpyGovernedAdjustmentPoster : IGovernedLedgerAdjustmentPoster
    {
        public List<Guid> Posted { get; } = new();

        public Task PostAdjustmentAsync(SecurityMasterRevisionPublishedEvent evt, Guid ledgerBookId, CancellationToken ct = default)
        {
            Posted.Add(ledgerBookId);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyRestatementProposer : IReportPackRestatementProposer
    {
        public List<ReportPackRestatementProposalDto> Proposals { get; } = new();

        public Task<ReportPackRestatementProposalDto> ProposeAsync(SecurityMasterRevisionPublishedEvent evt, Guid ledgerBookId, CancellationToken ct = default)
        {
            // BLUEPRINT: ReportPackRestatementProposalDto carries ChangedLines built from evt.ChangedFields.
            var proposal = new ReportPackRestatementProposalDto(
                ProposalId: Guid.NewGuid(),
                ReasonCode: "security-master-edit",
                ChangedLines: evt.ChangedFields.Select(f => new ReportPackChangedLineDto(
                    LineKey: f, FieldName: f, OldValue: null, NewValue: null)).ToArray());
            Proposals.Add(proposal);
            return Task.FromResult(proposal);
        }
    }
}
