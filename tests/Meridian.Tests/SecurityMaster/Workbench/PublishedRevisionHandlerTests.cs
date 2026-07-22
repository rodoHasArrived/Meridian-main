using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Phase 3 published-revision side-effect handlers: projection rebuild (Order=10) then coverage
/// invalidation (Order=20). Both must be idempotent and safe to run in composition roots where the
/// Security Master backend is not configured.
/// </summary>
public sealed class PublishedRevisionHandlerTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ProjectionRebuild_RunsFirst_CoverageInvalidation_RunsSecond()
    {
        new SecurityProjectionRebuildHandler(NullLogger<SecurityProjectionRebuildHandler>.Instance)
            .Order.Should().Be(10);
        new CoverageInvalidationHandler(
                new NullMultiAssetCoverageInvalidator(), NullLogger<CoverageInvalidationHandler>.Instance)
            .Order.Should().Be(20, "coverage invalidation must run after the projection it depends on is rebuilt");
    }

    [Fact]
    public async Task ProjectionRebuild_NoBackendConfigured_IsNoOp()
    {
        // With no rebuilder/cache (Security Master backend not configured) the handler must not throw —
        // the published-revision fan-out runs in composition roots that lack the backend.
        var handler = new SecurityProjectionRebuildHandler(NullLogger<SecurityProjectionRebuildHandler>.Instance);

        await handler.Invoking(h => h.HandleAsync(Event())).Should().NotThrowAsync();
    }

    [Fact]
    public async Task CoverageInvalidation_InvokesInvalidatorWithEvent()
    {
        var invalidator = new Mock<IMultiAssetCoverageInvalidator>(MockBehavior.Strict);
        invalidator
            .Setup(i => i.InvalidateAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new CoverageInvalidationHandler(
            invalidator.Object, NullLogger<CoverageInvalidationHandler>.Instance);
        var evt = Event();

        await handler.HandleAsync(evt);

        invalidator.Verify(i => i.InvalidateAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NullCoverageInvalidator_IsNoOp()
    {
        var invalidator = new NullMultiAssetCoverageInvalidator();

        await invalidator.Invoking(i => i.InvalidateAsync(Event())).Should().NotThrowAsync();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static SecurityMasterRevisionPublishedEvent Event()
        => new(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Version: 7,
            EffectiveFrom: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            ChangedFields: ["EconomicDefinition.Coupon"],
            DownstreamImpact: EmptyImpact(),
            AffectedLedgerBookIds: [],
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
}
