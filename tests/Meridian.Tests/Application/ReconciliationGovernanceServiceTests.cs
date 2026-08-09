using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Application;

public sealed class ReconciliationGovernanceServiceTests
{
    [Fact]
    public void PublicConstructors_PreserveLegacyClrSignatureAndRequireExplicitClockInjection()
    {
        var legacyConstructor = typeof(ReconciliationGovernanceService).GetConstructor(
        [
            typeof(IReconciliationRunRepository),
            typeof(IReconciliationGovernanceAuditStore)
        ]);
        var timeProviderConstructor = typeof(ReconciliationGovernanceService).GetConstructor(
        [
            typeof(IReconciliationRunRepository),
            typeof(IReconciliationGovernanceAuditStore),
            typeof(TimeProvider)
        ]);

        Assert.NotNull(legacyConstructor);
        Assert.True(legacyConstructor!.GetParameters()[1].IsOptional);
        Assert.Null(legacyConstructor.GetParameters()[1].DefaultValue);
        Assert.NotNull(timeProviderConstructor);
        Assert.All(
            timeProviderConstructor!.GetParameters(),
            parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public async Task EvaluateGateAsync_Blocks_WhenThresholdBreachedWithoutWaiver()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-1", "run-1", DateTimeOffset.UtcNow, null, null, 0, 1, 1, false, 0.01m, 5),
            [],
            [new ReconciliationBreakDto("b1", "Mismatch", ReconciliationBreakCategory.AmountMismatch, ReconciliationBreakStatus.Open, "", 1m, 2m, 1m, ReconciliationBreakSeverity.Critical, "x", null, null)]);
        await repo.SaveAsync(detail);

        var sut = new ReconciliationGovernanceService(repo);
        var result = await sut.EvaluateGateAsync("run-1", new ReconciliationPolicyThresholds(MaxOpenBreakCount: 0, MaxCriticalOpenBreakCount: 0, MaxAbsoluteVariance: 0.1m), false, false);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
    }

    [Fact]
    public async Task EvaluateGateAsync_ReviewRequired_WhenBreachedAndSecondaryApprovalSigned()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-2", "run-2", DateTimeOffset.UtcNow, null, null, 0, 1, 1, false, 0.01m, 5),
            [],
            [new ReconciliationBreakDto("b1", "Mismatch", ReconciliationBreakCategory.AmountMismatch, ReconciliationBreakStatus.Open, "", 1m, 2m, 1m, ReconciliationBreakSeverity.High, "x", null, null)]);
        await repo.SaveAsync(detail);

        var sut = new ReconciliationGovernanceService(repo);
        var result = await sut.EvaluateGateAsync("run-2", new ReconciliationPolicyThresholds(MaxOpenBreakCount: 0, MaxAbsoluteVariance: 0.1m), true, true);

        Assert.Equal(TradingAcceptanceGateStatusDto.ReviewRequired, result.Status);
        Assert.True(result.SecondaryApprovalRequired);
    }

    [Fact]
    public async Task EvaluateGateAsync_DoesNotWriteAudit_WhenWriteAuditDisabled()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-3", "run-3", DateTimeOffset.UtcNow, null, null, 0, 1, 0, false, 0.01m, 1),
            [],
            []);
        await repo.SaveAsync(detail);

        var auditStore = new TestReconciliationGovernanceAuditStore();
        var sut = new ReconciliationGovernanceService(repo, auditStore);

        _ = await sut.EvaluateGateAsync("run-3", new ReconciliationPolicyThresholds(), waiverRequested: false, secondaryApprovalSigned: false, writeAudit: false);

        Assert.Equal(0, auditStore.AppendCount);
    }

    [Fact]
    public async Task EvaluateGateAsync_WritesAudit_ByDefault()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-4", "run-4", DateTimeOffset.UtcNow, null, null, 0, 1, 0, false, 0.01m, 1),
            [],
            []);
        await repo.SaveAsync(detail);

        var auditStore = new TestReconciliationGovernanceAuditStore();
        var sut = new ReconciliationGovernanceService(repo, auditStore);

        _ = await sut.EvaluateGateAsync("run-4", new ReconciliationPolicyThresholds(), waiverRequested: false, secondaryApprovalSigned: false);

        Assert.Equal(1, auditStore.AppendCount);
    }

    [Fact]
    public async Task EvaluateGateAsync_DoesNotBreachAgePolicy_AtExactMaximumAge()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(CreateOpenBreakDetail("run-age-boundary", now, now.AddHours(-24)));
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var result = await sut.EvaluateGateAsync(
            "run-age-boundary",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 0,
                MaxAbsoluteVariance: 1m,
                MaxBreakAgeHours: 24),
            waiverRequested: false,
            secondaryApprovalSigned: false);

        Assert.Equal(TradingAcceptanceGateStatusDto.Ready, result.Status);
        Assert.Equal(24d, result.MaxObservedBreakAgeHours);
        Assert.Equal(TimeSpan.FromHours(24).Ticks, result.MaxObservedBreakAgeTicks);
        Assert.DoesNotContain("break-age", result.BreachReasons);
    }

    [Fact]
    public async Task EvaluateGateAsync_Blocks_WhenBreakAgeExceedsMaximum()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(CreateOpenBreakDetail("run-age-breached", now, now.AddHours(-24).AddSeconds(-1)));
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var result = await sut.EvaluateGateAsync(
            "run-age-breached",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 0,
                MaxAbsoluteVariance: 1m,
                MaxBreakAgeHours: 24),
            waiverRequested: false,
            secondaryApprovalSigned: false);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
        Assert.True(result.MaxObservedBreakAgeHours > 24d);
        Assert.Equal(
            TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)).Ticks,
            result.MaxObservedBreakAgeTicks);
        Assert.Contains("break-age", result.BreachReasons);
        Assert.Contains("1.00:00:01", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateGateAsync_BlocksFutureFirstObservationEvenWithApprovedWaiver()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(CreateOpenBreakDetail("run-future", now, now.AddMinutes(1)));
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var result = await sut.EvaluateGateAsync(
            "run-future",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 1,
                MaxAbsoluteVariance: 1m,
                MaxBreakAgeHours: 24),
            waiverRequested: true,
            secondaryApprovalSigned: true);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
        Assert.True(result.HasInvalidChronology);
        Assert.Equal(0, result.MaxObservedBreakAgeTicks);
        Assert.Contains("break-chronology-invalid", result.BreachReasons);
        Assert.True(result.SnapshotWasAuthoritative);
    }

    [Fact]
    public async Task EvaluateGateAsync_HoldsRepositoryLeaseThroughAuditCommit()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var evaluated = CreateOpenBreakDetail("run-leased", now, now.AddHours(-1));
        var repository = new InMemoryReconciliationRunRepository();
        await repository.SaveAsync(evaluated);
        var replacement = evaluated with
        {
            Summary = evaluated.Summary with
            {
                ReconciliationRunId = "reconciliation-run-leased-replacement",
                CreatedAt = now.AddMinutes(1)
            },
            Breaks =
            [
                evaluated.Breaks.Single() with
                {
                    ActualAmount = 3m,
                    Variance = 2m
                }
            ]
        };
        var auditStore = new BlockingReconciliationGovernanceAuditStore();
        var sut = new ReconciliationGovernanceService(
            repository,
            auditStore,
            timeProvider: new FixedTimeProvider(now));

        var evaluationTask = sut.EvaluateGateAsync(
            "run-leased",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 1,
                MaxAbsoluteVariance: 1m),
            waiverRequested: false,
            secondaryApprovalSigned: false);
        await auditStore.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var competingSave = repository.SaveWithFirstObservationContinuityAsync(replacement);
        try
        {
            Assert.False(competingSave.IsCompleted);
        }
        finally
        {
            auditStore.ReleaseAppend.TrySetResult(true);
        }

        var result = await evaluationTask;
        await competingSave;

        Assert.True(result.SnapshotWasAuthoritative);
        Assert.Equal(evaluated.Summary.ReconciliationRunId, result.ReconciliationRunId);
        Assert.Equal(
            replacement.Summary.ReconciliationRunId,
            (await repository.GetLatestForRunAsync("run-leased"))!.Summary.ReconciliationRunId);
    }

    [Fact]
    public async Task InMemoryRepository_LeaseCallbackReentryFailsFastInsteadOfDeadlocking()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryReconciliationRunRepository();
        await repository.SaveAsync(CreateOpenBreakDetail("run-reentrant-lease", now, now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ExecuteWithLatestForRunLeaseAsync(
                "run-reentrant-lease",
                async (_, ct) =>
                {
                    _ = await repository.GetLatestForRunAsync("run-reentrant-lease", ct);
                    return true;
                }));

        Assert.Contains("cannot re-enter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateGateAsync_FailsClosedWhenRepositoryCannotLeaseLatestSnapshot()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new LegacyLatestRepository(CreateOpenBreakDetail("run-legacy-lease", now, now));
        var sut = new ReconciliationGovernanceService(
            repository,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.EvaluateGateAsync(
                "run-legacy-lease",
                new ReconciliationPolicyThresholds(MaxOpenBreakCount: 1, MaxCriticalOpenBreakCount: 1, MaxAbsoluteVariance: 1m),
                waiverRequested: false,
                secondaryApprovalSigned: false));

        Assert.Contains("leased latest-snapshot reads", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateGateAsync_BlocksFirstObservationAfterSnapshotCreation()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var snapshotCreatedAt = now.AddHours(-2);
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(CreateOpenBreakDetail(
            "run-invalid-snapshot-chronology",
            snapshotCreatedAt,
            snapshotCreatedAt.AddMinutes(1)));
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var result = await sut.EvaluateGateAsync(
            "run-invalid-snapshot-chronology",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 1,
                MaxAbsoluteVariance: 1m,
                MaxBreakAgeHours: 24),
            waiverRequested: true,
            secondaryApprovalSigned: true);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
        Assert.True(result.HasInvalidChronology);
        Assert.Contains("break-chronology-invalid", result.BreachReasons);
    }

    public static IEnumerable<object[]> InvalidPolicies()
    {
        yield return [new ReconciliationPolicyThresholds(MaxOpenBreakCount: -1)];
        yield return [new ReconciliationPolicyThresholds(MaxCriticalOpenBreakCount: -1)];
        yield return [new ReconciliationPolicyThresholds(MaxAbsoluteVariance: -0.01m)];
        yield return [new ReconciliationPolicyThresholds(MaxBreakAgeHours: -1)];
        yield return [new ReconciliationPolicyThresholds(MaxBreakAgeHours: int.MaxValue)];
    }

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public async Task EvaluateGateAsync_RejectsInvalidPolicyThresholds(
        ReconciliationPolicyThresholds invalidPolicy)
    {
        var sut = new ReconciliationGovernanceService(new InMemoryReconciliationRunRepository());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.EvaluateGateAsync(
                "run-invalid-policy",
                invalidPolicy,
                waiverRequested: false,
                secondaryApprovalSigned: false));
    }

    [Fact]
    public async Task EvaluateGateAsync_TreatsDecimalMinimumVarianceAsMaterialInsteadOfOverflowing()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var detail = CreateOpenBreakDetail("run-decimal-min", now, now) with
        {
            Breaks = [CreateOpenBreakDetail("run-decimal-min", now, now).Breaks.Single() with { Variance = decimal.MinValue }]
        };
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(detail);
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));

        var result = await sut.EvaluateGateAsync(
            "run-decimal-min",
            new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 1,
                MaxAbsoluteVariance: 1m),
            waiverRequested: false,
            secondaryApprovalSigned: false);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
        Assert.Equal(decimal.MaxValue, result.MaxObservedAbsoluteVariance);
        Assert.Contains("absolute-variance", result.BreachReasons);
    }

    [Fact]
    public async Task EvaluateAndExportEvidence_BindsExactBoundaryPolicySnapshotAndBreachReasons()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var expectedAge = TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1));
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"meridian-reconciliation-evidence-{Guid.NewGuid():N}");
        var auditPath = Path.Combine(outputDirectory, "audit", "governance.jsonl");
        try
        {
            var repo = new InMemoryReconciliationRunRepository();
            await repo.SaveAsync(CreateOpenBreakDetail(
                "run-evidence-boundary",
                now,
                now - expectedAge));
            var policy = new ReconciliationPolicyThresholds(
                MaxOpenBreakCount: 1,
                MaxCriticalOpenBreakCount: 1,
                MaxAbsoluteVariance: 1m,
                MaxBreakAgeHours: 24,
                RequireSecondaryApprovalForWaivers: true);
            var sut = new ReconciliationGovernanceService(
                repo,
                new JsonlReconciliationGovernanceAuditStore(auditPath),
                new FixedTimeProvider(now));

            var result = await sut.EvaluateGateAsync(
                "run-evidence-boundary",
                policy,
                waiverRequested: false,
                secondaryApprovalSigned: false);
            var jsonPath = await ReconciliationGovernanceService.ExportEvidenceAsync(
                result,
                outputDirectory,
                "run-evidence-boundary");

            Assert.Equal(expectedAge.Ticks, result.MaxObservedBreakAgeTicks);
            Assert.Equal(now, result.EvaluatedAtUtc);
            Assert.Equal(policy, result.Policy);
            Assert.Collection(
                result.BreachReasons,
                reason => Assert.Equal("break-age", reason));
            Assert.True(result.SnapshotWasAuthoritative);
            Assert.NotNull(result.ReconciliationSnapshotFingerprint);
            Assert.True(result.ReconciliationSnapshotFingerprint!.StartsWith("sha256:", StringComparison.Ordinal));

            var auditLines = await File.ReadAllLinesAsync(auditPath);
            Assert.Single(auditLines);
            using (var auditDocument = JsonDocument.Parse(auditLines[0]))
            {
                var audit = auditDocument.RootElement;
                Assert.Equal("run-evidence-boundary", audit.GetProperty("strategyRunId").GetString());
                Assert.Equal(result.ReconciliationRunId, audit.GetProperty("reconciliationRunId").GetString());
                Assert.Equal(
                    result.ReconciliationSnapshotFingerprint,
                    audit.GetProperty("reconciliationSnapshotFingerprint").GetString());
                Assert.Equal(now, audit.GetProperty("asOf").GetDateTimeOffset());
                Assert.Equal(24, audit.GetProperty("policy").GetProperty("maxBreakAgeHours").GetInt32());
                Assert.False(audit.GetProperty("waiverRequested").GetBoolean());
                Assert.False(audit.GetProperty("secondaryApprovalSigned").GetBoolean());
                Assert.True(audit.GetProperty("snapshotWasAuthoritative").GetBoolean());
                Assert.Equal(expectedAge.Ticks, audit.GetProperty("maxObservedBreakAgeTicks").GetInt64());
                Assert.Equal(
                    "break-age",
                    audit.GetProperty("breachReasons")[0].GetString());
            }

            using (var evidenceDocument = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath)))
            {
                var evidence = evidenceDocument.RootElement;
                Assert.Equal("run-evidence-boundary", evidence.GetProperty("StrategyRunId").GetString());
                Assert.Equal(result.ReconciliationRunId, evidence.GetProperty("ReconciliationRunId").GetString());
                Assert.Equal(expectedAge.Ticks, evidence.GetProperty("MaxObservedBreakAgeTicks").GetInt64());
                Assert.False(evidence.TryGetProperty("strategyRunId", out _));
            }

            var markdown = await File.ReadAllTextAsync(Path.ChangeExtension(jsonPath, ".md"));
            Assert.Contains("1.00:00:01", markdown, StringComparison.Ordinal);
            Assert.Contains("Policy max break age (hours): 24", markdown, StringComparison.Ordinal);
            Assert.Contains(result.ReconciliationRunId!, markdown, StringComparison.Ordinal);
            Assert.Contains(result.ReconciliationSnapshotFingerprint!, markdown, StringComparison.Ordinal);
            Assert.Contains("Breach reasons: break-age", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExportEvidenceAsync_RejectsRunIdThatDoesNotMatchBoundEvaluation()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var repo = new InMemoryReconciliationRunRepository();
        await repo.SaveAsync(CreateOpenBreakDetail("run-bound", now, now));
        var sut = new ReconciliationGovernanceService(
            repo,
            auditStore: null,
            timeProvider: new FixedTimeProvider(now));
        var evaluation = await sut.EvaluateGateAsync(
            "run-bound",
            new ReconciliationPolicyThresholds(MaxOpenBreakCount: 1, MaxCriticalOpenBreakCount: 1, MaxAbsoluteVariance: 1m),
            waiverRequested: false,
            secondaryApprovalSigned: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReconciliationGovernanceService.ExportEvidenceAsync(
                evaluation,
                Path.GetTempPath(),
                "different-run"));
    }

    [Fact]
    public async Task JsonlAuditStore_RepairsTornTrailingRecordBeforeAppending()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"meridian-reconciliation-audit-recovery-{Guid.NewGuid():N}");
        var auditPath = Path.Combine(outputDirectory, "governance.jsonl");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await File.WriteAllTextAsync(
                auditPath,
                "{\"evidenceVersion\":0}" + Environment.NewLine + "{\"evidenceVersion\":");
            var store = new JsonlReconciliationGovernanceAuditStore(auditPath);

            await store.AppendAsync(CreateEvaluation("run-recovered-audit"));

            var lines = await File.ReadAllLinesAsync(auditPath);
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            });
            using var appended = JsonDocument.Parse(lines[1]);
            Assert.Equal(1, appended.RootElement.GetProperty("evidenceVersion").GetInt32());
            Assert.Equal(
                "run-recovered-audit",
                appended.RootElement.GetProperty("strategyRunId").GetString());
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonlAuditStore_CompletesTornCrLfTerminatorWithoutCreatingEmptyLine()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"meridian-reconciliation-audit-crlf-recovery-{Guid.NewGuid():N}");
        var auditPath = Path.Combine(outputDirectory, "governance.jsonl");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await File.WriteAllTextAsync(auditPath, "{\"evidenceVersion\":0}\r");
            var store = new JsonlReconciliationGovernanceAuditStore(auditPath);

            await store.AppendAsync(CreateEvaluation("run-recovered-crlf-audit"));

            var lines = await File.ReadAllLinesAsync(auditPath);
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
            Assert.All(lines, line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            });
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonlAuditStore_SerializesConcurrentWritersWithoutInterleavedRecords()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"meridian-reconciliation-audit-concurrency-{Guid.NewGuid():N}");
        var auditPath = Path.Combine(outputDirectory, "governance.jsonl");
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
                new JsonlReconciliationGovernanceAuditStore(auditPath).AppendAsync(
                    CreateEvaluation($"run-concurrent-audit-{index}"))));

            var lines = await File.ReadAllLinesAsync(auditPath);
            Assert.Equal(16, lines.Length);
            var runIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                runIds.Add(document.RootElement.GetProperty("strategyRunId").GetString()!);
            }

            Assert.Equal(16, runIds.Count);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static ReconciliationRunDetail CreateOpenBreakDetail(
        string runId,
        DateTimeOffset createdAt,
        DateTimeOffset firstObservedAt) => new(
            new ReconciliationRunSummary(
                $"reconciliation-{runId}",
                runId,
                createdAt,
                null,
                null,
                0,
                1,
                1,
                false,
                0.01m,
                5),
            [],
            [
                new ReconciliationBreakDto(
                    "cash-balance",
                    "Cash balance",
                    ReconciliationBreakCategory.AmountMismatch,
                    ReconciliationBreakStatus.Open,
                    "ledger",
                    1m,
                    2m,
                    1m,
                    ReconciliationBreakSeverity.High,
                    "Cash differs.",
                    null,
                    null)
                {
                    FirstObservedAt = firstObservedAt
                }
            ]);

    private static ReconciliationGateEvaluation CreateEvaluation(string runId) => new(
        TradingAcceptanceGateStatusDto.Ready,
        "Reconciliation policy within threshold.",
        0,
        0,
        0m,
        false)
    {
        StrategyRunId = runId,
        ReconciliationRunId = $"reconciliation-{runId}",
        ReconciliationRunCreatedAt = new DateTimeOffset(2026, 8, 5, 11, 0, 0, TimeSpan.Zero),
        EvaluatedAtUtc = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        Policy = new ReconciliationPolicyThresholds(),
        SnapshotWasAuthoritative = true,
        ReconciliationSnapshotFingerprint = "sha256:test"
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestReconciliationGovernanceAuditStore : IReconciliationGovernanceAuditStore
    {
        public int AppendCount { get; private set; }

        public Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default)
        {
            AppendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingReconciliationGovernanceAuditStore : IReconciliationGovernanceAuditStore
    {
        public TaskCompletionSource<bool> AppendStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseAppend { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AppendAsync(
            ReconciliationGateEvaluation evaluation,
            CancellationToken ct = default)
        {
            AppendStarted.TrySetResult(true);
            await ReleaseAppend.Task.WaitAsync(ct);
        }
    }

    private sealed class LegacyLatestRepository(ReconciliationRunDetail detail) : IReconciliationRunRepository
    {
        public Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationRunDetail?> GetByIdAsync(
            string reconciliationRunId,
            CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(detail);

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
    }

}
