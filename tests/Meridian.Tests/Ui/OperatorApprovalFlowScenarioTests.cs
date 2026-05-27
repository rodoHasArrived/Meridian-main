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
/// W2 Operator Approval Flow Scenario Test.
/// Walks the full operator path from an empty readiness state through session
/// creation, replay verification, and audit confirmation — proving that the
/// paper-trading cockpit reaches ReadyForPaperOperation=true via the three
/// core gates (session, replay, audit-controls) that gate the initial
/// operator sign-off cycle.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class OperatorApprovalFlowScenarioTests
{
    [Fact]
    public async Task FullOperatorApprovalFlow_EmptyStateToReadyForPaperOperation()
    {
        // ---- STEP 1: empty state — nothing is ready yet ----

        await using var auditTrail = CreateAuditTrail(nameof(FullOperatorApprovalFlow_EmptyStateToReadyForPaperOperation));

        await using var emptyProvider = new ServiceCollection()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            emptyProvider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var emptyReadiness = await readinessService.GetAsync();

        emptyReadiness.ReadyForPaperOperation.Should().BeFalse("nothing has been set up yet");
        emptyReadiness.ActiveSession.Should().BeNull("no session exists");

        // ---- STEP 2: create a paper session ----

        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("w2-approval-scenario", "W2 Approval Flow", 100_000m));

        session.IsActive.Should().BeTrue("the session should be active after creation");

        // ---- STEP 3: record a trade action — satisfies the audit-controls gate ----
        //
        // The audit-controls gate requires at least one audit entry to be visible so
        // operators can demonstrate the session has observable activity. A non-Control
        // category entry is enough: it brings auditEntries.Count above zero while
        // leaving no unexplained control evidence to block the gate.

        await auditTrail.RecordAsync(
            category: "Trade",
            action: "Execution",
            outcome: "Filled",
            actor: "algo",
            orderId: "order-w2-001",
            correlationId: "corr-w2-001");

        // ---- STEP 4: verify the replay — satisfies the replay gate ----

        var replay = await sessionService.VerifyReplayAsync(
            session.SessionId,
            expectedFillCount: 0,
            expectedOrderCount: 0);

        replay.Should().NotBeNull("replay verification should produce a result");
        replay!.IsConsistent.Should().BeTrue("no fills yet so counts match");

        // ---- STEP 5: check overall readiness ----
        //
        // With no StrategyRunReadService registered the readiness service operates in
        // 'active session only' mode. In that mode it returns Ready once the session,
        // replay, and audit-controls gates are all satisfied — brokerage sync and
        // promotion gates are deferred to the live-run stage.

        await using var readyProvider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .AddSingleton<ExecutionOperatorControlService>()
            .BuildServiceProvider();
        var readyReadinessService = new TradingOperatorReadinessService(
            readyProvider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await readyReadinessService.GetAsync();

        // Core assertion: the cockpit is ready for paper operation.
        readiness.ReadyForPaperOperation.Should().BeTrue(
            "session is active, replay is verified, and audit trail has visible activity");

        // Session gate
        var sessionGate = readiness.AcceptanceGates.FirstOrDefault(g => g.GateId == "session");
        sessionGate.Should().NotBeNull();
        sessionGate!.Status.Should().Be(TradingAcceptanceGateStatusDto.Ready);

        // Replay gate
        var replayGate = readiness.AcceptanceGates.FirstOrDefault(g => g.GateId == "replay");
        replayGate.Should().NotBeNull();
        replayGate!.Status.Should().Be(TradingAcceptanceGateStatusDto.Ready);

        // Audit-controls gate
        var auditGate = readiness.AcceptanceGates.FirstOrDefault(g => g.GateId == "audit-controls");
        auditGate.Should().NotBeNull();
        auditGate!.Status.Should().Be(TradingAcceptanceGateStatusDto.Ready);

        // Brokerage-sync gate is pending (no live sync configured) — not a blocker here.
        var brokerageGate = readiness.AcceptanceGates.FirstOrDefault(g => g.GateId == "brokerage-sync");
        brokerageGate.Should().NotBeNull();
        brokerageGate!.Status.Should().NotBe(TradingAcceptanceGateStatusDto.Blocked,
            "brokerage-sync should be pending/review-required, not a hard block at this stage");
    }

    [Fact]
    public async Task FullOperatorApprovalFlow_SessionWithFillsAndReplayConfirmReadiness()
    {
        // Arrange: operator has already traded and wants to confirm the session is traceable.

        await using var auditTrail = CreateAuditTrail(nameof(FullOperatorApprovalFlow_SessionWithFillsAndReplayConfirmReadiness));
        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("w2-fills-scenario", "W2 Fills Flow", 50_000m)
            {
                Symbols = ["AAPL", "MSFT"]
            });

        // Record a fill on each symbol.
        foreach (var symbol in new[] { "AAPL", "MSFT" })
        {
            var fill = new ExecutionFill
            {
                SessionId = session.SessionId,
                Symbol = symbol,
                Quantity = 5,
                FillPrice = symbol == "AAPL" ? 175m : 420m,
                FilledAt = DateTimeOffset.UtcNow,
                FillType = FillType.Trade
            };
            await sessionService.RecordPaperFillAsync(session.SessionId, fill);

            await auditTrail.RecordAsync(
                category: "Trade",
                action: "Execution",
                outcome: "Filled",
                actor: "algo",
                orderId: $"order-{symbol.ToLowerInvariant()}",
                correlationId: $"corr-{symbol.ToLowerInvariant()}");
        }

        // Verify replay with the exact fill counts that were recorded.
        var replay = await sessionService.VerifyReplayAsync(
            session.SessionId,
            expectedFillCount: 2,
            expectedOrderCount: 0);

        replay!.IsConsistent.Should().BeTrue("fill counts must match what the session recorded");
        replay.VerifiedFilledCount.Should().Be(2);

        // Check readiness.
        await using var provider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .AddSingleton<ExecutionOperatorControlService>()
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await readinessService.GetAsync();

        readiness.ReadyForPaperOperation.Should().BeTrue(
            "fills recorded, replay verified, and audit trail has entries for both symbols");
        readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readiness.Replay!.IsConsistent.Should().BeTrue();
    }

    private static ExecutionAuditTrailService CreateAuditTrail(string testName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "meridian-tests", testName);
        Directory.CreateDirectory(tempDir);
        return new ExecutionAuditTrailService(tempDir, NullLogger<ExecutionAuditTrailService>.Instance);
    }
}
