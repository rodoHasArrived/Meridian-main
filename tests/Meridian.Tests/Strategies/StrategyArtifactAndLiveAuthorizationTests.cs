using System.Globalization;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Storage.Operations;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task StrategyArtifactRegistry_CanonicalizesInputsAndRetainsVersionsAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "strategy-artifacts", Guid.NewGuid().ToString("N"));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var artifact = new StrategyArtifact("buy-and-hold", StrategyAuthoringRoute.Code, "revision-1",
                new Dictionary<string, string> { ["quantity"] = "10", ["period"] = "5" },
                ["MSFT", "AAPL"], "daily", "cost-model-v1",
                new Dictionary<string, string> { ["maxDrawdown"] = "0.50" });
            var registry = new StrategyArtifactRegistry(root);
            var firstId = await registry.RetainAsync(artifact);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var reordered = artifact with
            {
                Parameters = new Dictionary<string, string> { ["period"] = "5", ["quantity"] = "10" },
                Universe = ["aapl", "MSFT", "AAPL"]
            };
            (await registry.RetainAsync(reordered)).Should().Be(firstId);
            var changed = artifact with { ExecutionRealism = "cost-model-v2" };
            var changedId = await registry.RetainAsync(changed);
            changedId.Should().NotBe(firstId);

            var runs = new StrategyRunStore(new FileOperationalCaseHistoryStore(root));
            var run = StrategyRunEntry.Start("buy-and-hold", "Buy and Hold", RunType.Backtest).WithStrategyArtifact(firstId);
            await runs.RecordRunAsync(run);
            var recovered = await new StrategyRunStore(new FileOperationalCaseHistoryStore(root)).GetRunByIdAsync(run.RunId);
            recovered!.StrategyArtifactId.Should().Be(firstId);
            var reopened = new StrategyArtifactRegistry(root);
            (await reopened.GetAsync(firstId))!.ExecutionRealism.Should().Be("cost-model-v1");
            (await reopened.GetAsync(changedId))!.ExecutionRealism.Should().Be("cost-model-v2");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Authorization_ExpiryAndRevocationBlockDispatchAndReplay()
    {
        var root = Path.Combine(Path.GetTempPath(), "strategy-authorization", Guid.NewGuid().ToString("N"));
        try
        {
            var account = Guid.Parse("bec00700-f1a1-4000-8000-000000000002");
            var now = DateTimeOffset.UtcNow;
            var clock = new RehearsalClock(now);
            var registry = new StrategyArtifactRegistry(root);
            var strategy = new BuyAndHoldLiveStrategy();
            var parameters = new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "10" };
            var artifactId = await registry.RetainAsync(StrategyArtifact.FromCode(strategy.StrategyId, strategy, parameters,
                new BacktestRequest(new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7), Symbols: ["AAPL"])));
            var runs = new StrategyRunStore(new FileOperationalCaseHistoryStore(root));
            var history = new JsonlPromotionRecordStore(Path.Combine(root, "promotions"), NullLogger<JsonlPromotionRecordStore>.Instance);
            var paper = StrategyRunEntry.StartForFundAccount(strategy.StrategyId, strategy.Name, RunType.Paper,
                account, parameterSet: parameters).WithStrategyArtifact(artifactId).Complete(BuildPassingResult()) with
            {
                WalkForwardEvidence = new(1.1d, 0.08m, 0.85d, 6, now, "synthetic")
            };
            await runs.RecordRunAsync(paper);
            await using var broker = new RehearsalBroker();
            var controls = new ExecutionOperatorControlService(new(Path.Combine(root, "controls")),
                NullLogger<ExecutionOperatorControlService>.Instance);
            var allow = await controls.CreateManualOverrideAsync(new(ExecutionManualOverrideKinds.AllowLivePromotion,
                "Synthetic approval", "synthetic-reviewer", StrategyId: paper.StrategyId, RunId: paper.RunId));
            StrategyLiveAuthorizationService NewGate() => new(root, runs, history, registry, broker, clock,
                (id, _) => Task.FromResult(id == account));
            var gate = NewGate();
            var promotions = new PromotionService(runs, new(), history, NullLogger<PromotionService>.Instance,
                controls, brokerageConfiguration: CreateLiveBrokerageConfiguration(),
                trustEvidenceProvider: new PromotionTrustEvidenceStub(), liveAuthorizations: gate);
            var expiry = now.AddHours(1);
            var request = RehearsalApproval(paper.RunId, RunType.Live, allow.OverrideId)
                with
            { LiveAuthorization = new(artifactId, ["AAPL"], 1_200m, "USD", expiry) };
            var approval = await promotions.ApproveAsync(request);
            approval.Success.Should().BeTrue(approval.Reason);
            var retained = (await history.LoadAllAsync()).Should().ContainSingle().Subject;
            var runId = retained.TargetRunId!;

            await using (var overLimit = await gate.AcquireAsync(runId, artifactId, account, "AAPL", () => 1_201m, default))
                overLimit.Refusal.Should().Contain("notional");
            clock.UtcNow = expiry;
            await using (var expired = await gate.AcquireAsync(runId, artifactId, account, "AAPL", () => 1_000m, default))
                expired.Refusal.Should().Contain("expired");
            var renewal = request with { LiveAuthorization = request.LiveAuthorization! with { ExpiresAt = expiry.AddHours(1) } };
            (await promotions.ApproveAsync(renewal)).Reason.Should().Contain("expired").And.Contain("fresh Paper run");
            clock.UtcNow = now;

            Task<StrategyLiveAuthorizationRevocation> revoke;
            await using (var dispatch = await gate.AcquireAsync(runId, artifactId, account, "AAPL", () => 1_000m, default))
            {
                dispatch.Refusal.Should().BeNull();
                revoke = promotions.RevokeLiveAuthorizationAsync(retained, "synthetic-reviewer", "Stop synthetic authorization", default);
                revoke.IsCompleted.Should().BeFalse();
            }
            var receipt = await revoke.WaitAsync(TimeSpan.FromSeconds(5));
            (await promotions.RevokeLiveAuthorizationAsync(retained, "synthetic-reviewer", "Retried", default)).Should().Be(receipt);
            var revokedApproval = await promotions.ApproveAsync(renewal);
            revokedApproval.Success.Should().BeFalse();
            revokedApproval.Reason.Should().Contain("revoked").And.Contain("fresh Paper run");
            (await history.LoadAllAsync()).Should().ContainSingle();
            await using var restarted = await NewGate().AcquireAsync(runId, artifactId, account, "AAPL", () => 1_000m, default);
            restarted.Refusal.Should().Contain("revoked");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
