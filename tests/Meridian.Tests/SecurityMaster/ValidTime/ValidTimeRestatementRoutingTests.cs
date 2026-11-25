using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging.Abstractions;
using static Meridian.Tests.SecurityMaster.ValidTime.ValidTimeTestData;

namespace Meridian.Tests.SecurityMaster.ValidTime;

/// <summary>
/// ADR-034 restatement routing: a term change recorded as an economic event governs only until the next
/// later valid-from, so routing carries that window to candidate location and the persisted decision.
/// </summary>
public sealed class ValidTimeRestatementRoutingTests
{
    private static readonly Guid SecurityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Book = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CorrectionFrom = Utc(2026, 3, 15);

    [Fact]
    public async Task CorrectionBoundedByLaterValidFrom_RoutesTheWindow()
    {
        var reader = new FakeValidTimeReader(
            Segment(Definition(SecurityId, 5m), 1, Utc(2025, 1, 1), CorrectionFrom),
            Segment(Definition(SecurityId, 5.25m), 3, CorrectionFrom, Utc(2026, 6, 1)),
            Segment(Definition(SecurityId, 6m), 2, Utc(2026, 6, 1), null));
        var candidates = new CapturingCandidateResolver();
        var resolver = NewResolver(candidates, reader);

        var decision = await resolver.ResolveAsync(Event(version: 3, CorrectionFrom));

        candidates.GovernedUntil.Should().Be(new DateOnly(2026, 6, 1));
        candidates.UsedWindowOverload.Should().BeTrue();
        decision.GovernedFrom.Should().Be(new DateOnly(2026, 3, 15));
        decision.GovernedUntil.Should().Be(new DateOnly(2026, 6, 1));
        decision.RestatementRequired.Should().BeTrue("the book is hard-closed at the effective date");
    }

    [Fact]
    public async Task LatestValidFrom_IsOpenEnded()
    {
        var reader = new FakeValidTimeReader(
            Segment(Definition(SecurityId, 5m), 1, Utc(2025, 1, 1), CorrectionFrom),
            Segment(Definition(SecurityId, 5.25m), 2, CorrectionFrom, null));
        var candidates = new CapturingCandidateResolver();

        var decision = await NewResolver(candidates, reader).ResolveAsync(Event(version: 2, CorrectionFrom));

        decision.GovernedUntil.Should().BeNull();
        candidates.UsedWindowOverload.Should().BeFalse("an open-ended change is routed exactly as before");
    }

    [Fact]
    public async Task ValidToWithTimeOfDay_KeepsThatDayInsideTheWindow()
    {
        var reader = new FakeValidTimeReader(
            Segment(Definition(SecurityId, 5.25m), 2, CorrectionFrom, new DateTimeOffset(2026, 6, 1, 14, 30, 0, TimeSpan.Zero)),
            Segment(Definition(SecurityId, 6m), 3, new DateTimeOffset(2026, 6, 1, 14, 30, 0, TimeSpan.Zero), null));

        var decision = await NewResolver(new CapturingCandidateResolver(), reader).ResolveAsync(Event(version: 3, CorrectionFrom));

        decision.GovernedUntil.Should().Be(new DateOnly(2026, 6, 2));
    }

    [Fact]
    public async Task ChangeNotInTheTimeline_StaysOpenEnded()
    {
        // An overlay-only edit, or a change whose effective instant no economic event asserts.
        var reader = new FakeValidTimeReader(
            Segment(Definition(SecurityId, 5m), 1, Utc(2025, 1, 1), Utc(2026, 6, 1)),
            Segment(Definition(SecurityId, 6m), 2, Utc(2026, 6, 1), null));

        var decision = await NewResolver(new CapturingCandidateResolver(), reader).ResolveAsync(Event(version: 2, CorrectionFrom));

        decision.GovernedUntil.Should().BeNull();
    }

    [Fact]
    public async Task CorporateActionRouting_VersionZero_IsNotBounded()
    {
        var reader = new FakeValidTimeReader(
            Segment(Definition(SecurityId, 5.25m), 2, CorrectionFrom, Utc(2026, 6, 1)));

        var decision = await NewResolver(new CapturingCandidateResolver(), reader).ResolveAsync(Event(version: 0, CorrectionFrom));

        decision.GovernedUntil.Should().BeNull("a corporate action is not a term event in the valid-time timeline");
    }

    [Fact]
    public async Task WithoutReader_RoutingIsUnchanged()
    {
        var candidates = new CapturingCandidateResolver();

        var decision = await NewResolver(candidates, validTime: null).ResolveAsync(Event(version: 3, CorrectionFrom));

        decision.GovernedUntil.Should().BeNull();
        candidates.UsedWindowOverload.Should().BeFalse();
        decision.RestatementRequired.Should().BeTrue();
    }

    private static PeriodAwareRestatementResolver NewResolver(IRestatementCandidateResolver candidates, Meridian.Contracts.SecurityMaster.ISecurityTermsValidTimeReader? validTime)
        => new(new HardClosedLockReader(), candidates, NullLogger<PeriodAwareRestatementResolver>.Instance, validTime);

    private static SecurityMasterRevisionPublishedEvent Event(long version, DateTimeOffset effectiveFrom)
        => new(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Version: version,
            EffectiveFrom: effectiveFrom,
            ChangedFields: ["EconomicDefinition.Coupon"],
            DownstreamImpact: new SecurityMasterDownstreamImpactDto(
                FundProfileId: "fund-alpha",
                IsScoped: true,
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
                Links: []),
            AffectedLedgerBookIds: [Book],
            Actor: "ops.analyst",
            CorrelationId: null);

    private sealed class HardClosedLockReader : ILedgerPeriodLockReader
    {
        public Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(Guid ledgerBookId, DateOnly date, CancellationToken ct = default)
            => Task.FromResult(LedgerPeriodStatusDto.HardClosed);
    }

    private sealed class CapturingCandidateResolver : IRestatementCandidateResolver
    {
        public DateOnly? GovernedUntil { get; private set; }

        public bool UsedWindowOverload { get; private set; }

        public Task<RestatementCandidateResult> ResolveAsync(
            Guid securityId, Guid ledgerBookId, DateOnly effectiveDate, IReadOnlyList<string> changedFields,
            string? fundProfileId, CancellationToken ct = default)
            => Task.FromResult(RestatementCandidateResult.Empty);

        public Task<RestatementCandidateResult> ResolveAsync(
            Guid securityId, Guid ledgerBookId, DateOnly effectiveDate, DateOnly? governedUntil, IReadOnlyList<string> changedFields,
            string? fundProfileId, CancellationToken ct = default)
        {
            UsedWindowOverload = true;
            GovernedUntil = governedUntil;
            return Task.FromResult(RestatementCandidateResult.Empty);
        }
    }
}
