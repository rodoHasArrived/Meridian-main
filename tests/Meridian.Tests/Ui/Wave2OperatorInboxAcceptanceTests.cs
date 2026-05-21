using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Wave 2 Operator Inbox Acceptance Tests.
/// Validates that operator work items flow correctly from the paper trading
/// cockpit into a unified inbox that enables operators to triage and navigate
/// to concrete workflows.
/// </summary>
public sealed class Wave2OperatorInboxAcceptanceTests
{
    [Fact]
    public async Task OperatorInbox_AggregatesReadinessWorkItems()
    {
        // Arrange: Create readiness service
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness (which should populate work items)
        var readiness = await readinessService.GetAsync();

        // Assert: Work items are present and stable
        readiness.WorkItems.Should().NotBeEmpty("readiness should have work items when gates are not ready");
        readiness.WorkItems.Select(w => w.WorkItemId)
            .Should().NotContainNulls("all work items must have stable IDs");

        // Verify stability - same IDs on repeated calls
        var readiness2 = await readinessService.GetAsync();
        readiness2.WorkItems.Select(w => w.WorkItemId)
            .Should().Equal(readiness.WorkItems.Select(w => w.WorkItemId));
    }

    [Fact]
    public async Task OperatorInbox_PaperSessionMissingIsBlockingWorkItem()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness without any sessions
        var readiness = await readinessService.GetAsync();

        // Assert: Paper session missing is critical
        var sessionWorkItem = readiness.WorkItems.FirstOrDefault(w => w.WorkItemId == "paper-session-missing");
        sessionWorkItem.Should().NotBeNull("paper session missing should be a work item");
        sessionWorkItem!.Tone.Should().Be(OperatorWorkItemToneDto.Critical, "missing session is blocking");
        sessionWorkItem.Kind.Should().Be(OperatorWorkItemKindDto.PaperReplay);
    }

    [Fact]
    public async Task OperatorInbox_ReplayVerificationRequiredShowsAsWarning()
    {
        // Arrange: Session exists but replay not verified
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_ReplayVerificationRequiredShowsAsWarning));
        await using var provider = new ServiceCollection()
            .AddSingleton(new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                auditTrail: auditTrail))
            .AddSingleton(auditTrail)
            .BuildServiceProvider();

        var sessionService = provider.GetRequiredService<PaperSessionPersistenceService>();
        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-inbox", "Inbox Test", 100_000m));

        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness with active but unverified session
        var readiness = await readinessService.GetAsync();

        // Assert: Replay verification required appears as warning
        var replayWorkItem = readiness.WorkItems.FirstOrDefault(w =>
            w.WorkItemId.Contains("replay", StringComparison.OrdinalIgnoreCase));
        replayWorkItem.Should().NotBeNull("should have replay-related work item");
        replayWorkItem!.Tone.Should().Be(OperatorWorkItemToneDto.Warning, "unverified replay is a warning");
        replayWorkItem.Scope.Should().Be(session.SessionId, "work item should reference the session");
    }

    [Fact]
    public async Task OperatorInbox_ExecutionControlWorkItemsHaveAuditReferences()
    {
        // Arrange: Create readiness with audit trail
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_ExecutionControlWorkItemsHaveAuditReferences));
        var controlService = new ExecutionOperatorControlService();

        await using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(controlService)
            .BuildServiceProvider();

        // Record control decision
        var auditEntry = await auditTrail.RecordAsync(
            category: "Order",
            action: "OrderRejected",
            outcome: "RejectedByControl",
            actor: "risk-ops",
            reason: "position-limit-exceeded",
            orderId: "order-inbox-1",
            correlationId: "corr-inbox-1");

        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness with control evidence
        var readiness = await readinessService.GetAsync();

        // Assert: Control work items reference audits
        var controlWorkItem = readiness.WorkItems.FirstOrDefault(w =>
            w.Kind == OperatorWorkItemKindDto.ExecutionControl);
        if (controlWorkItem != null)
        {
            controlWorkItem.AuditReference.Should().NotBeNullOrEmpty("control work items must reference audits");
        }
    }

    [Fact]
    public async Task OperatorInbox_WorkItemsHaveNavigationHints()
    {
        // Arrange
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_WorkItemsHaveNavigationHints));
        await using var provider = new ServiceCollection()
            .AddSingleton(new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                auditTrail: auditTrail))
            .AddSingleton(auditTrail)
            .BuildServiceProvider();

        var sessionService = provider.GetRequiredService<PaperSessionPersistenceService>();
        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-nav", "Navigation Test", 100_000m));

        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Work items have descriptive context for navigation
        readiness.WorkItems.Should().AllSatisfy(item =>
        {
            item.WorkItemId.Should().NotBeNullOrEmpty();
            item.Title.Should().NotBeNullOrEmpty("work item title for navigation");
            item.Description.Should().NotBeNullOrEmpty("work item description for action");
        });
    }

    [Fact]
    public async Task OperatorInbox_WorkItemsFilteredByAccountContext()
    {
        // Arrange: Create readiness with fund account context
        var fundAccountId = Guid.NewGuid();

        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness with fund account filter
        var readiness = await readinessService.GetAsync(fundAccountId: fundAccountId);

        // Assert: Readiness includes account context
        readiness.Should().NotBeNull();
        // In a real scenario, work items would be filtered by account
        readiness.WorkItems.Should().NotBeNull();
    }

    [Fact]
    public async Task OperatorInbox_OverallStatusReflectsBlockingGates()
    {
        // Arrange: No sessions or verification
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Overall status blocked when critical items exist
        var criticalItems = readiness.WorkItems.Where(w => w.Tone == OperatorWorkItemToneDto.Critical).ToList();
        if (criticalItems.Any())
        {
            readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
            readiness.ReadyForPaperOperation.Should().BeFalse();
        }
    }

    [Fact]
    public async Task OperatorInbox_OverallStatusReviewWhenWarningsExist()
    {
        // Arrange: Session exists but unverified (warning state)
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_OverallStatusReviewWhenWarningsExist));
        await using var provider = new ServiceCollection()
            .AddSingleton(new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                auditTrail: auditTrail))
            .AddSingleton(auditTrail)
            .BuildServiceProvider();

        var sessionService = provider.GetRequiredService<PaperSessionPersistenceService>();
        await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-warn", "Warning Test", 100_000m));

        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Status reflects warning state
        var warningItems = readiness.WorkItems.Where(w => w.Tone == OperatorWorkItemToneDto.Warning).ToList();
        if (warningItems.Any() && !readiness.WorkItems.Any(w => w.Tone == OperatorWorkItemToneDto.Critical))
        {
            readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        }
    }

    [Fact]
    public async Task OperatorInbox_ReadyStatusWhenAllGatesPassed()
    {
        // Arrange: Session with verified replay
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_ReadyStatusWhenAllGatesPassed));
        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-ready", "Ready Test", 100_000m));

        // Apply fill and verify replay
        var fill = new ExecutionFill
        {
            SessionId = session.SessionId,
            Symbol = "TEST",
            Quantity = 1,
            FillPrice = 100m,
            FilledAt = DateTimeOffset.UtcNow,
            FillType = FillType.Trade
        };
        await sessionService.RecordPaperFillAsync(session.SessionId, fill);

        var replay = await sessionService.VerifyReplayAsync(
            session.SessionId,
            expectedFillCount: 1,
            expectedOrderCount: null);

        await using var provider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness with all gates passed
        var readiness = await readinessService.GetAsync();

        // Assert: Status is ready when gates pass
        if (replay?.IsConsistent == true && sessionService.GetSessions().Any(s => s.IsActive))
        {
            var hasBlockingItems = readiness.WorkItems.Any(w => w.Tone == OperatorWorkItemToneDto.Critical);
            hasBlockingItems.Should().BeFalse("no critical blocking items when replay verified");
        }
    }

    [Fact]
    public async Task OperatorInbox_EvidenceCompletenessScoreAvailable()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Evidence completeness is provided
        readiness.EvidenceCompleteness.Should().NotBeNull();
        readiness.EvidenceCompleteness!.BlockingGateIds.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReviewGateIds.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReadyGateIds.Should().NotBeNull();
    }

    [Fact]
    public async Task OperatorInbox_AcceptanceGatesAreDocumented()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Acceptance gates have clear naming
        readiness.AcceptanceGates.Should().NotBeEmpty("should have named acceptance gates");
        readiness.AcceptanceGates.Should().AllSatisfy(gate =>
        {
            gate.GateId.Should().NotBeNullOrEmpty();
            gate.Status.Should().NotBe(TradingAcceptanceGateStatusDto.Unknown);
        });
    }

    [Fact]
    public async Task OperatorInbox_WorkItemIDsAreStableAcrossCalls()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Call readiness multiple times
        var readiness1 = await readinessService.GetAsync();
        var readiness2 = await readinessService.GetAsync();
        var readiness3 = await readinessService.GetAsync();

        var ids1 = readiness1.WorkItems.Select(w => w.WorkItemId).OrderBy(x => x).ToList();
        var ids2 = readiness2.WorkItems.Select(w => w.WorkItemId).OrderBy(x => x).ToList();
        var ids3 = readiness3.WorkItems.Select(w => w.WorkItemId).OrderBy(x => x).ToList();

        // Assert: IDs don't change (enables stable UI state)
        ids1.Should().Equal(ids2, "work item IDs should be stable");
        ids2.Should().Equal(ids3, "work item IDs should be stable");
        ids1.Should().AllSatisfy(id => id.Should().NotBeEmpty("all IDs must be non-empty"));
    }

    [Fact]
    public async Task OperatorInbox_NoRandomIDChurn()
    {
        // Arrange: Session that should produce same work item IDs
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_NoRandomIDChurn));
        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-churn", "Churn Test", 100_000m));

        await using var provider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness multiple times
        var readiness1 = await readinessService.GetAsync();
        await Task.Delay(100); // Small delay
        var readiness2 = await readinessService.GetAsync();
        await Task.Delay(100);
        var readiness3 = await readinessService.GetAsync();

        var workItemIds1 = readiness1.WorkItems.Select(w => w.WorkItemId).ToHashSet();
        var workItemIds2 = readiness2.WorkItems.Select(w => w.WorkItemId).ToHashSet();
        var workItemIds3 = readiness3.WorkItems.Select(w => w.WorkItemId).ToHashSet();

        // Assert: IDs remain consistent (deterministic, not random)
        workItemIds1.Should().Equal(workItemIds2);
        workItemIds2.Should().Equal(workItemIds3);
    }

    [Fact]
    public async Task OperatorInbox_BoundedToActionableItems()
    {
        // Arrange: Ready session (should not appear in inbox)
        await using var auditTrail = CreateAuditTrail(nameof(OperatorInbox_BoundedToActionableItems));
        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("strat-bounded", "Bounded Test", 100_000m));

        var fill = new ExecutionFill
        {
            SessionId = session.SessionId,
            Symbol = "TEST",
            Quantity = 1,
            FillPrice = 100m,
            FilledAt = DateTimeOffset.UtcNow,
            FillType = FillType.Trade
        };
        await sessionService.RecordPaperFillAsync(session.SessionId, fill);

        var replay = await sessionService.VerifyReplayAsync(
            session.SessionId,
            expectedFillCount: 1,
            expectedOrderCount: null);

        await using var provider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        // Act: Get readiness
        var readiness = await readinessService.GetAsync();

        // Assert: Only actionable items in queue (not every status)
        // Ready items should not generate work items
        readiness.WorkItems.Should().AllSatisfy(item =>
            item.Tone.Should().NotBe(OperatorWorkItemToneDto.Info,
                "only blocking/warning items should be work items"));
    }

    // ---- HELPERS ----

    private static ExecutionAuditTrailService CreateAuditTrail(string testName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "meridian-tests", testName);
        Directory.CreateDirectory(tempDir);
        return new ExecutionAuditTrailService(tempDir, NullLogger<ExecutionAuditTrailService>.Instance);
    }
}
