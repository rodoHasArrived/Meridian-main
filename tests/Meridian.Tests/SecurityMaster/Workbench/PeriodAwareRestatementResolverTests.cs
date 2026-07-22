using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Phase 3 period-aware propagation routing (D3 matrix): a closed period is never silently mutated —
/// hard-closed (or indeterminate/default-deny) exposure becomes a mandatory restatement proposal, while
/// open periods propagate without restatement.
/// </summary>
public sealed class PeriodAwareRestatementResolverTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BookB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task NoAffectedBooks_NoRestatement_AndDoesNotConsultLockReader()
    {
        var lockReader = new FakeLockReader(_ => throw new InvalidOperationException("must not be consulted when no books are affected"));
        var resolver = NewResolver(lockReader, NoCandidates());

        var decision = await resolver.ResolveAsync(Event(affectedBooks: []));

        decision.RestatementRequired.Should().BeFalse();
        decision.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenPeriod_NoRestatement()
    {
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.Open), NoCandidates());

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeFalse();
        decision.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task HardClosedPeriod_RequiresRestatement_WithLocatedCandidates()
    {
        var candidate = Candidate();
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.HardClosed), Candidates(candidate));

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeTrue();
        decision.Candidates.Should().ContainSingle().Which.Should().Be(candidate);
    }

    [Fact]
    public async Task HardClosedPeriod_NoCandidatesLocated_StillRequiresRestatement()
    {
        // Exposure exists but no pack could be located → manual locate-affected-packs task, never silent completion.
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.HardClosed), NoCandidates());

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeTrue();
        decision.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task SoftClosedPeriod_WithPublishedExposure_ProposesRestatementForThosePacks()
    {
        var candidate = Candidate();
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.SoftClosed), Candidates(candidate));

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeTrue("an already-published pack consumed the soft-closed line");
        decision.Candidates.Should().ContainSingle().Which.Should().Be(candidate);
    }

    [Fact]
    public async Task SoftClosedPeriod_NoPublishedExposure_NoRestatement()
    {
        // Soft-closed posts as a governed adjustment (a later slice); with no published pack, nothing to restate.
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.SoftClosed), NoCandidates());

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeFalse();
        decision.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task SoftClosedPeriod_NonActionableMatch_FlagsManualRestatement()
    {
        // A soft-closed book whose only match is non-actionable (e.g. already-restated, which the workflow
        // cannot re-restate) must still be flagged for manual follow-up rather than reported as no
        // restatement. The soft-closed arm does not default-deny, so this relies on the resolver's
        // HasNonActionableMatches signal.
        var resolver = NewResolver(
            new FakeLockReader(_ => LedgerPeriodStatusDto.SoftClosed),
            new FakeCandidateResolver([], hasNonActionableMatches: true));

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA]));

        decision.RestatementRequired.Should().BeTrue("a soft-closed non-actionable match still needs manual follow-up");
        decision.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleBooks_OneHardClosed_AggregatesToRestatementRequired()
    {
        var lockReader = new FakeLockReader(book => book == BookB ? LedgerPeriodStatusDto.HardClosed : LedgerPeriodStatusDto.Open);
        var resolver = NewResolver(lockReader, NoCandidates());

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA, BookB]));

        decision.RestatementRequired.Should().BeTrue("any hard-closed affected book forces a restatement proposal");
    }

    [Fact]
    public async Task MultipleBooks_SameFundCandidate_IsDeduplicatedByReport()
    {
        // The candidate resolver locates fund-scoped published packs, so the same report surfaces from
        // every affected book; the decision must collapse them to one proposal per report.
        var candidate = Candidate();
        var resolver = NewResolver(new FakeLockReader(_ => LedgerPeriodStatusDto.HardClosed), Candidates(candidate));

        var decision = await resolver.ResolveAsync(Event(affectedBooks: [BookA, BookB]));

        decision.RestatementRequired.Should().BeTrue();
        decision.Candidates.Should().ContainSingle().Which.Should().Be(candidate);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static PeriodAwareRestatementResolver NewResolver(
        ILedgerPeriodLockReader lockReader, IRestatementCandidateResolver candidateResolver)
        => new(lockReader, candidateResolver, NullLogger<PeriodAwareRestatementResolver>.Instance);

    private static IRestatementCandidateResolver NoCandidates() => new FakeCandidateResolver([]);

    private static IRestatementCandidateResolver Candidates(params RestatementCandidateDto[] candidates)
        => new FakeCandidateResolver(candidates);

    private static RestatementCandidateDto Candidate()
        => new(
            ReportId: Guid.NewGuid(),
            PriorVersionReportId: Guid.NewGuid(),
            PeriodLabel: "2026-P03",
            Summary: "Restate pack for corrected reference data.",
            ChangedLines: []);

    private static SecurityMasterRevisionPublishedEvent Event(IReadOnlyList<Guid> affectedBooks)
        => new(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Version: 7,
            EffectiveFrom: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            ChangedFields: ["EconomicDefinition.Coupon"],
            DownstreamImpact: EmptyImpact(),
            AffectedLedgerBookIds: affectedBooks,
            Actor: "ops.analyst",
            CorrelationId: null);

    private static SecurityMasterDownstreamImpactDto EmptyImpact()
        => new(
            FundProfileId: null,
            IsScoped: false,
            Severity: SecurityMasterImpactSeverity.None,
            Summary: "n/a",
            PortfolioExposureSummary: string.Empty,
            LedgerExposureSummary: string.Empty,
            ReconciliationExposureSummary: string.Empty,
            ReportPackExposureSummary: string.Empty,
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);

    private sealed class FakeLockReader : ILedgerPeriodLockReader
    {
        private readonly Func<Guid, LedgerPeriodStatusDto> _map;
        public FakeLockReader(Func<Guid, LedgerPeriodStatusDto> map) => _map = map;

        public Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(Guid ledgerBookId, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(_map(ledgerBookId));
    }

    private sealed class FakeCandidateResolver : IRestatementCandidateResolver
    {
        private readonly IReadOnlyList<RestatementCandidateDto> _candidates;
        private readonly bool _hasNonActionableMatches;

        public FakeCandidateResolver(IReadOnlyList<RestatementCandidateDto> candidates, bool hasNonActionableMatches = false)
        {
            _candidates = candidates;
            _hasNonActionableMatches = hasNonActionableMatches;
        }

        public Task<RestatementCandidateResult> ResolveAsync(
            Guid securityId, Guid ledgerBookId, DateOnly effectiveDate, IReadOnlyList<string> changedFields,
            string? fundProfileId, CancellationToken ct = default)
            => Task.FromResult(new RestatementCandidateResult(_candidates, _hasNonActionableMatches));
    }
}
