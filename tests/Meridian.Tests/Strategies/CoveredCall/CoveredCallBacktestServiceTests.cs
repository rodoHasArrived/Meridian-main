using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Workstation;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Operations;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services.CoveredCall;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Meridian.Tests.Strategies.CoveredCall;

/// <summary>
/// Guards covered-call engine completion and durable recovery when the process restarts or the
/// terminal strategy-run evidence append fails after a valid backtest has executed.
/// </summary>
public sealed class CoveredCallBacktestServiceTests
{
    private const string DefaultVaultId = "ev-0123456789abcdef01234567";
    private const string MissingVaultId = "ev-fedcba987654321001234567";

    private static readonly CoveredCallRunScope Scope =
        new("tenant-alpha", "company-alpha", "strategy-operator");

    public static TheoryData<string, int> EvidenceCategoryCountLimits =>
        new()
        {
            { nameof(CoveredCallBacktestRequest.OperatorAcceptanceCriteria), CoveredCallBacktestService.MaxOperatorAcceptanceCriteriaCount },
            { nameof(CoveredCallBacktestRequest.RetainedEvidenceReferences), CoveredCallBacktestService.MaxRetainedEvidenceReferenceCount },
            { nameof(CoveredCallBacktestRequest.AccountingRecordReferences), CoveredCallBacktestService.MaxAccountingRecordReferenceCount },
            { nameof(CoveredCallBacktestRequest.ApprovalReferences), CoveredCallBacktestService.MaxApprovalReferenceCount },
            { nameof(CoveredCallBacktestRequest.PaperValidationReferences), CoveredCallBacktestService.MaxPaperValidationReferenceCount },
            { nameof(CoveredCallBacktestRequest.GovernedReportReferences), CoveredCallBacktestService.MaxGovernedReportReferenceCount }
        };

    public static TheoryData<string, int> EvidenceCategoryValueLengthLimits =>
        new()
        {
            { nameof(CoveredCallBacktestRequest.OperatorAcceptanceCriteria), CoveredCallBacktestService.MaxOperatorAcceptanceCriterionLength },
            { nameof(CoveredCallBacktestRequest.RetainedEvidenceReferences), CoveredCallBacktestService.MaxEvidenceReferenceLength },
            { nameof(CoveredCallBacktestRequest.AccountingRecordReferences), CoveredCallBacktestService.MaxEvidenceReferenceLength },
            { nameof(CoveredCallBacktestRequest.ApprovalReferences), CoveredCallBacktestService.MaxEvidenceReferenceLength },
            { nameof(CoveredCallBacktestRequest.PaperValidationReferences), CoveredCallBacktestService.MaxEvidenceReferenceLength },
            { nameof(CoveredCallBacktestRequest.GovernedReportReferences), CoveredCallBacktestService.MaxEvidenceReferenceLength }
        };

    [Fact]
    public async Task Scenario_SameScopeEvidenceVaultAuthority_AllowsQueueAndDisposesManifest()
    {
        var manifestContent = new TrackingStream();
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceManifestFile(
                manifestContent,
                "application/json",
                "manifest.json",
                DateTimeOffset.UtcNow));
        var repository = new Mock<IStrategyRepository>(MockBehavior.Strict);
        var timeProvider = new CountingTimeProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService(
            "unused-market-data",
            repository.Object,
            cache,
            evidenceStore.Object,
            timeProvider);

        var handle = await service.StartAsync(
            CreateRequest(
            [
                "evidence://strategy-runs/covered-call-overwrite",
                $"evidence://evidence-vault/{DefaultVaultId}".ToUpperInvariant()
            ]),
            Scope);

        handle.RunId.Should().NotBeNullOrWhiteSpace();
        manifestContent.IsDisposed.Should().BeTrue();
        timeProvider.UtcNowReadCount.Should().BeGreaterThan(0);
        (await service.GetStatusAsync(handle.RunId, Scope)).Should().Match<CoveredCallRunStatusDto>(
            status => status.Phase == "Queued");
        repository.VerifyNoOtherCalls();
        evidenceStore.VerifyAll();
    }

    [Fact]
    public async Task StartAsync_WithoutCanonicalVaultReference_FailsBeforeQueueOrDurableMutation()
    {
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);

        await AssertAuthorityFailureBeforeMutationAsync(
            CreateRequest(["evidence://strategy-runs/covered-call-overwrite"]),
            evidenceStore.Object);
    }

    [Theory]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567/extra")]
    [InlineData("evidence://evidence-vault/%2e%2e")]
    [InlineData("evidence://evidence-vault/%252e%252e")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef012345%2f")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef012345%252f")]
    [InlineData("evidence://evidence-vault/vault-covered-call")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef0123456g")]
    [InlineData("evidence://evidence-vault:444/ev-0123456789abcdef01234567")]
    [InlineData("evidence://operator@evidence-vault/ev-0123456789abcdef01234567")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567?download=true")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567#fragment")]
    public async Task StartAsync_WithMalformedVaultReference_FailsBeforeQueueOrDurableMutation(
        string retainedEvidenceReference)
    {
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);

        await AssertAuthorityFailureBeforeMutationAsync(
            CreateRequest([retainedEvidenceReference]),
            evidenceStore.Object);
    }

    [Fact]
    public async Task StartAsync_WhenCanonicalManifestIsMissing_FailsBeforeQueueOrDurableMutation()
    {
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvidenceManifestFile?)null);

        await AssertAuthorityFailureBeforeMutationAsync(CreateRequest(), evidenceStore.Object);

        evidenceStore.VerifyAll();
    }

    [Fact]
    public async Task StartAsync_WhenManifestExistsOnlyInForeignScope_FailsClosed()
    {
        var foreignContent = new TrackingStream();
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvidenceManifestFile?)null);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                "tenant-foreign",
                "company-foreign",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceManifestFile(
                foreignContent,
                "application/json",
                "manifest.json",
                DateTimeOffset.UtcNow));

        await AssertAuthorityFailureBeforeMutationAsync(CreateRequest(), evidenceStore.Object);

        foreignContent.IsDisposed.Should().BeFalse("foreign-scope evidence must never be opened");
        evidenceStore.Verify(store => store.TryOpenManifestByVaultIdAsync(
            DefaultVaultId,
            Scope.TenantId,
            Scope.CompanyId,
            It.IsAny<CancellationToken>()), Times.Once);
        evidenceStore.Verify(store => store.TryOpenManifestByVaultIdAsync(
            DefaultVaultId,
            "tenant-foreign",
            "company-foreign",
            It.IsAny<CancellationToken>()), Times.Never);
        foreignContent.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenAnyCanonicalManifestIsMissing_RejectsEntireRequest()
    {
        var manifestContent = new TrackingStream();
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceManifestFile(
                manifestContent,
                "application/json",
                "manifest.json",
                DateTimeOffset.UtcNow));
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                MissingVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvidenceManifestFile?)null);

        await AssertAuthorityFailureBeforeMutationAsync(
            CreateRequest(
            [
                $"evidence://evidence-vault/{DefaultVaultId}",
                $"evidence://evidence-vault/{MissingVaultId}"
            ]),
            evidenceStore.Object);

        manifestContent.IsDisposed.Should().BeTrue();
        evidenceStore.VerifyAll();
    }

    [Fact]
    public async Task StartAsync_WhenAuthorityLookupIsCancelled_PropagatesCancellationBeforeMutation()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, CancellationToken ct) =>
                WaitForCancellationAsync(lookupStarted, ct));
        var repository = new Mock<IStrategyRepository>(MockBehavior.Strict);
        var timeProvider = new CountingTimeProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService(
            "unused-market-data",
            repository.Object,
            cache,
            evidenceStore.Object,
            timeProvider);
        using var cancellation = new CancellationTokenSource();

        var startTask = service.StartAsync(CreateRequest(), Scope, cancellation.Token).AsTask();
        await lookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Func<Task> action = async () => await startTask;
        await action.Should().ThrowAsync<OperationCanceledException>();
        timeProvider.UtcNowReadCount.Should().Be(0);
        repository.VerifyNoOtherCalls();
        evidenceStore.VerifyAll();
    }

    [Theory]
    [MemberData(nameof(EvidenceCategoryCountLimits))]
    public async Task StartAsync_WhenEvidenceCategoryExceedsCountBudget_FailsBeforeStoreIo(
        string category,
        int maxCount)
    {
        var oversizedValues = Enumerable.Range(0, maxCount + 1)
            .Select(index => $"value-{index}")
            .ToArray();
        var request = WithEvidenceValues(CreateRequest(), category, oversizedValues);
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);

        await AssertAuthorityFailureBeforeMutationAsync(request, evidenceStore.Object);
    }

    [Theory]
    [MemberData(nameof(EvidenceCategoryValueLengthLimits))]
    public async Task StartAsync_WhenEvidenceValueExceedsLengthBudget_FailsBeforeStoreIo(
        string category,
        int maxLength)
    {
        var request = WithEvidenceValues(
            CreateRequest(),
            category,
            [new string('x', maxLength + 1)]);
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);

        await AssertAuthorityFailureBeforeMutationAsync(request, evidenceStore.Object);
    }

    [Fact]
    public async Task StartAsync_AtEveryCategoryCountBoundary_AllowsQueue()
    {
        var request = CreateRequest() with
        {
            OperatorAcceptanceCriteria = Enumerable.Range(
                    0,
                    CoveredCallBacktestService.MaxOperatorAcceptanceCriteriaCount)
                .Select(index => $"criterion-{index}")
                .ToArray(),
            RetainedEvidenceReferences =
            [
                $"evidence://evidence-vault/{DefaultVaultId}",
                .. Enumerable.Range(1, CoveredCallBacktestService.MaxRetainedEvidenceReferenceCount - 1)
                    .Select(index => $"evidence://generic-{index}/retained")
            ],
            AccountingRecordReferences = Enumerable.Range(
                    0,
                    CoveredCallBacktestService.MaxAccountingRecordReferenceCount)
                .Select(index => $"ledger://accounting/{index}")
                .ToArray(),
            ApprovalReferences = Enumerable.Range(
                    0,
                    CoveredCallBacktestService.MaxApprovalReferenceCount)
                .Select(index => $"approval://strategy/{index}")
                .ToArray(),
            PaperValidationReferences = Enumerable.Range(
                    0,
                    CoveredCallBacktestService.MaxPaperValidationReferenceCount)
                .Select(index => $"workflow://paper/{index}")
                .ToArray(),
            GovernedReportReferences = Enumerable.Range(
                    0,
                    CoveredCallBacktestService.MaxGovernedReportReferenceCount)
                .Select(index => $"reporting-run://governed/{index}")
                .ToArray()
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService(
            "unused-market-data",
            new Mock<IStrategyRepository>(MockBehavior.Strict).Object,
            cache);

        var handle = await service.StartAsync(request, Scope);

        handle.RunId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StartAsync_AtPerValueAndAggregateCharacterBoundaries_AllowsQueue()
    {
        var canonicalReference = $"evidence://evidence-vault/{DefaultVaultId}";
        var criteria = Enumerable.Range(0, CoveredCallBacktestService.MaxOperatorAcceptanceCriteriaCount)
            .Select(_ => new string('c', CoveredCallBacktestService.MaxOperatorAcceptanceCriterionLength))
            .ToArray();
        var referenceBudget = CoveredCallBacktestService.MaxAggregateEvidenceCharacters
            - criteria.Sum(static value => value.Length)
            - canonicalReference.Length;
        var fullReferenceCount = referenceBudget / CoveredCallBacktestService.MaxEvidenceReferenceLength;
        var finalReferenceLength = referenceBudget % CoveredCallBacktestService.MaxEvidenceReferenceLength;
        var retainedReferences = new List<string> { canonicalReference };
        retainedReferences.AddRange(Enumerable.Range(0, fullReferenceCount)
            .Select(index => CreateEvidenceReferenceWithLength(
                "evidence",
                index,
                CoveredCallBacktestService.MaxEvidenceReferenceLength)));
        if (finalReferenceLength > 0)
        {
            retainedReferences.Add(CreateEvidenceReferenceWithLength("evidence", 99, finalReferenceLength));
        }

        var request = CreateRequest(retainedReferences) with
        {
            OperatorAcceptanceCriteria = criteria
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService(
            "unused-market-data",
            new Mock<IStrategyRepository>(MockBehavior.Strict).Object,
            cache);

        var handle = await service.StartAsync(request, Scope);

        handle.RunId.Should().NotBeNullOrWhiteSpace();
        criteria.Sum(static value => value.Length)
            .Should().Be(CoveredCallBacktestService.MaxOperatorAcceptanceCriteriaCount *
                         CoveredCallBacktestService.MaxOperatorAcceptanceCriterionLength);
        (criteria.Sum(static value => value.Length) + retainedReferences.Sum(static value => value.Length))
            .Should().Be(CoveredCallBacktestService.MaxAggregateEvidenceCharacters);
    }

    [Fact]
    public async Task StartAsync_WhenAggregateEvidenceBudgetIsExceeded_FailsBeforeStoreIo()
    {
        var criteria = Enumerable.Range(0, CoveredCallBacktestService.MaxOperatorAcceptanceCriteriaCount)
            .Select(_ => new string('c', CoveredCallBacktestService.MaxOperatorAcceptanceCriterionLength))
            .ToArray();
        var oversizedReferences = Enumerable.Range(0, 9)
            .Select(index => CreateEvidenceReferenceWithLength(
                "evidence",
                index,
                CoveredCallBacktestService.MaxEvidenceReferenceLength))
            .Prepend($"evidence://evidence-vault/{DefaultVaultId}")
            .ToArray();
        var request = CreateRequest(oversizedReferences) with
        {
            OperatorAcceptanceCriteria = criteria
        };
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);

        await AssertAuthorityFailureBeforeMutationAsync(request, evidenceStore.Object);
    }

    [Fact]
    public async Task Scenario_CoveredCallRunCompletes_ResultSurvivesRestartWithinTenantScope()
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);
        WriteBarJsonl(dataRoot, "SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), 475m);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            var recordingStore = new RecordingStrategyRepository(durableStore);
            string runId;
            CoveredCallRunResult retainedResult;

            using (var cache = new MemoryCache(new MemoryCacheOptions()))
            await using (var service = CreateService(dataRoot, recordingStore, cache))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await service.StartAsync(timeout.Token);

                ICoveredCallBacktestService contract = service;
#pragma warning disable CS0618 // Explicitly prove the obsolete unscoped compatibility shim fails closed.
                Action startWithoutScope = () => _ = contract.StartAsync(CreateRequest(), timeout.Token);
#pragma warning restore CS0618
                startWithoutScope.Should().Throw<NotSupportedException>()
                    .WithMessage("*authenticated tenant and company scope*");

                var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
                runId = handle.RunId;
                var status = await WaitForTerminalAsync(service, runId, Scope, timeout.Token);

                status.Phase.Should().Be("Completed");
                status.FailureMessage.Should().BeNull();
                retainedResult = (await service.GetResultAsync(runId, Scope, timeout.Token))!;
                retainedResult.Should().NotBeNull();
                retainedResult.RunId.Should().Be(runId);
            }

            var started = recordingStore.Attempts
                .Single(entry => entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Started);
            var completed = recordingStore.Attempts
                .Single(entry => entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Completed);

            completed.ParameterSet.Should().BeEquivalentTo(started.ParameterSet);
            completed.InputHashSha256.Should().Be(started.InputHashSha256);
            completed.InputHashSha256.Should().Be(StrategyRunEntry.ComputeEvidenceBoundInputHash(
                completed.StrategyId,
                completed.StrategyName,
                completed.RunType,
                completed.DatasetReference,
                completed.FeedReference,
                completed.Engine,
                completed.ParameterSet,
                completed.ParentRunId,
                completed.PortfolioId,
                completed.LedgerReference,
                completed.AuditReference,
                completed.FundProfileId,
                completed.OperatorAcceptanceCriteria,
                completed.RetainedEvidenceReferences,
                completed.AccountingRecordReferences,
                completed.ApprovalReferences,
                completed.PaperValidationReferences,
                completed.GovernedReportReferences));
            completed.ParameterSet.Should().NotContainKey(CoveredCallBacktestService.PersistedResultParameterKey);
            completed.ParameterSet.Should().NotContainKey("cagr");
            completed.ParameterSet.Should().NotContainKey("sharpe");
            completed.ParameterSet.Should().NotContainKey("winRate");
            completed.OutputMetadata.Should().ContainKey(CoveredCallBacktestService.PersistedResultParameterKey);
            completed.OutputMetadata.Should().ContainKeys("cagr", "sharpe", "winRate");

            var restartedStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            using var restartedCache = new MemoryCache(new MemoryCacheOptions());
            await using var restartedService = CreateService(dataRoot, restartedStore, restartedCache);
            using var restartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var rehydrated = await restartedService.GetResultAsync(runId, Scope, restartTimeout.Token);
            var foreignTenant = Scope with { TenantId = "tenant-foreign" };
            var foreignCompany = Scope with { CompanyId = "company-foreign" };

            rehydrated.Should().BeEquivalentTo(retainedResult);
            (await restartedService.GetResultAsync(runId, foreignTenant, restartTimeout.Token)).Should().BeNull();
            (await restartedService.GetResultAsync(runId, foreignCompany, restartTimeout.Token)).Should().BeNull();
            (await restartedService.ListRunsAsync(Scope, ct: restartTimeout.Token))
                .Should().ContainSingle(summary => summary.RunId == runId && summary.Status == "Completed");
            (await restartedService.ListRunsAsync(foreignTenant, ct: restartTimeout.Token)).Should().BeEmpty();
            (await restartedService.ListRunsAsync(foreignCompany, ct: restartTimeout.Token)).Should().BeEmpty();
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    [Fact]
    public async Task Scenario_TerminalEvidenceAppendFails_RunFailsClosedWithoutCachedResult()
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);
        WriteBarJsonl(dataRoot, "SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), 475m);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            var rejectingStore = new RecordingStrategyRepository(durableStore, rejectCompleted: true);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            await using var service = CreateService(dataRoot, rejectingStore, cache);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StartAsync(timeout.Token);

            var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
            var status = await WaitForTerminalAsync(service, handle.RunId, Scope, timeout.Token);

            status.Phase.Should().Be("Failed");
            status.FailureMessage.Should().Contain("simulated terminal completion persistence failure");
            rejectingStore.Attempts.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                StrategyRunLifecycleEventType.Started,
                StrategyRunLifecycleEventType.Completed,
                StrategyRunLifecycleEventType.Failed);
            rejectingStore.SuccessfulAppends.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                [StrategyRunLifecycleEventType.Started, StrategyRunLifecycleEventType.Failed],
                "failed append diagnostics: {0}",
                string.Join(" | ", rejectingStore.FailedAppendMessages));
            rejectingStore.Attempts.Single(entry => entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Completed)
                .OutputMetadata.Should().ContainKey(CoveredCallBacktestService.PersistedResultParameterKey);

            var retainedFailure = await durableStore.GetRunByIdAsync(handle.RunId, timeout.Token);
            retainedFailure.Should().NotBeNull();
            retainedFailure!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
            retainedFailure.OutputMetadata.Should().BeEmpty();
            (await service.GetResultAsync(handle.RunId, Scope, timeout.Token)).Should().BeNull();
            cache.Count.Should().Be(0, "a result is published only after the Completed append succeeds");
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    [Fact]
    public async Task Scenario_CompletionAndFailureEvidenceAppendsFail_RunReportsPersistenceDegradedWithoutTerminalClaim()
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);
        WriteBarJsonl(dataRoot, "SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), 475m);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            var rejectingStore = new RecordingStrategyRepository(
                durableStore,
                rejectCompleted: true,
                rejectFailed: true);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            await using var service = CreateService(dataRoot, rejectingStore, cache);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StartAsync(timeout.Token);

            var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
            var status = await WaitForPhaseAsync(
                service,
                handle.RunId,
                Scope,
                "PersistenceDegraded",
                timeout.Token);

            status.FailureMessage.Should().Contain("simulated terminal completion persistence failure");
            status.FailureMessage.Should().Contain("simulated terminal failure persistence failure");
            rejectingStore.Attempts.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                StrategyRunLifecycleEventType.Started,
                StrategyRunLifecycleEventType.Completed,
                StrategyRunLifecycleEventType.Failed);
            rejectingStore.SuccessfulAppends.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                StrategyRunLifecycleEventType.Started);

            var retainedRun = await durableStore.GetRunByIdAsync(handle.RunId, timeout.Token);
            retainedRun.Should().NotBeNull();
            retainedRun!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Started,
                "neither attempted terminal lifecycle event was durably appended");
            (await service.GetResultAsync(handle.RunId, Scope, timeout.Token)).Should().BeNull();
            cache.Count.Should().Be(0);
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    [Fact]
    public async Task Scenario_CancellationEvidenceAppendFails_RunReportsPersistenceDegradedInsteadOfCancelled()
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            var rejectingStore = new RecordingStrategyRepository(durableStore, rejectCancelled: true);
            var chainFactory = new CancellationBarrierChainProviderFactory();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            await using var service = CreateService(
                dataRoot,
                rejectingStore,
                cache,
                chainFactory: chainFactory);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StartAsync(timeout.Token);

            var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
            await chainFactory.CreateEntered.Task.WaitAsync(timeout.Token);
            await service.CancelAsync(handle.RunId, Scope, timeout.Token);
            var status = await WaitForPhaseAsync(
                service,
                handle.RunId,
                Scope,
                "PersistenceDegraded",
                timeout.Token);

            status.Phase.Should().NotBe("Cancelled");
            status.FailureMessage.Should().Contain("durable Cancelled lifecycle append failed");
            rejectingStore.Attempts.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                StrategyRunLifecycleEventType.Started,
                StrategyRunLifecycleEventType.Cancelled);
            rejectingStore.SuccessfulAppends.Select(entry => entry.LastLifecycleEvent).Should().Equal(
                StrategyRunLifecycleEventType.Started);
            (await durableStore.GetRunByIdAsync(handle.RunId, timeout.Token))!
                .LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Started);
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    [Fact]
    public async Task StartAsync_WhenQueueIsClosed_ReportsPersistenceDegradedWithoutFailedLifecycleClaim()
    {
        var repository = new Mock<IStrategyRepository>(MockBehavior.Strict);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService("unused-market-data", repository.Object, cache);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var handle = await service.StartAsync(CreateRequest(), Scope);
        var status = await service.GetStatusAsync(handle.RunId, Scope);

        status.Should().NotBeNull();
        status!.Phase.Should().Be("PersistenceDegraded");
        status.Phase.Should().NotBe("Failed");
        status.FailureMessage.Should().Contain("not queued");
        status.FailureMessage.Should().Contain("no durable lifecycle entry");
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Scenario_ResultCacheThrows_DurableCompletionAndRehydratedResultRemainAvailable()
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);
        WriteBarJsonl(dataRoot, "SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), 475m);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            using var cache = new ThrowingMemoryCache();
            await using var service = CreateService(dataRoot, durableStore, cache);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StartAsync(timeout.Token);

            var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
            var status = await WaitForTerminalAsync(service, handle.RunId, Scope, timeout.Token);
            var result = await service.GetResultAsync(handle.RunId, Scope, timeout.Token);
            var summaries = await service.ListRunsAsync(Scope, ct: timeout.Token);

            status.Phase.Should().Be("Completed");
            status.FailureMessage.Should().BeNull();
            result.Should().NotBeNull();
            result!.RunId.Should().Be(handle.RunId);
            summaries.Should().ContainSingle(summary =>
                summary.RunId == handle.RunId && summary.Status == "Completed");
            (await durableStore.GetRunByIdAsync(handle.RunId, timeout.Token))!
                .LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Scenario_ResultCacheDurationReloadIsNonPositive_ServiceNormalizesAndCachesCompletedResult(
        long durationTicks)
    {
        var scenarioRoot = CreateScenarioRoot();
        var dataRoot = Path.Combine(scenarioRoot, "market-data");
        var historyRoot = Path.Combine(scenarioRoot, "operations");
        Directory.CreateDirectory(dataRoot);
        WriteBarJsonl(dataRoot, "SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), 475m);

        try
        {
            var durableStore = new StrategyRunStore(new FileOperationalCaseHistoryStore(historyRoot));
            var optionsMonitor = new ReloadableOptionsMonitor<CoveredCallBacktestOptions>(CreateOptions(dataRoot));
            using var cache = new MemoryCache(new MemoryCacheOptions());
            await using var service = CreateService(
                dataRoot,
                durableStore,
                cache,
                optionsMonitor: optionsMonitor);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StartAsync(timeout.Token);
            optionsMonitor.Reload(new CoveredCallBacktestOptions
            {
                DataRootOverride = dataRoot,
                MaxConcurrentRuns = 1,
                ResultCacheDuration = TimeSpan.FromTicks(durationTicks)
            });

            var handle = await service.StartAsync(CreateRequest(), Scope, timeout.Token);
            var status = await WaitForTerminalAsync(service, handle.RunId, Scope, timeout.Token);
            var cacheKey = $"covered-call-result:{CoveredCallBacktestService.GetScopedStrategyId(Scope)}:{handle.RunId}";

            status.Phase.Should().Be("Completed");
            cache.TryGetValue(cacheKey, out CoveredCallRunResult? cached).Should().BeTrue(
                "non-positive hot-reloaded cache durations are normalized to the safe default");
            cached!.RunId.Should().Be(handle.RunId);
        }
        finally
        {
            DeleteScenarioRoot(scenarioRoot);
        }
    }

    private static CoveredCallBacktestService CreateService(
        string dataRoot,
        IStrategyRepository repository,
        IMemoryCache cache,
        IEvidenceArtifactStore? evidenceArtifactStore = null,
        TimeProvider? timeProvider = null,
        IOptionsMonitor<CoveredCallBacktestOptions>? optionsMonitor = null,
        ICoveredCallChainProviderFactory? chainFactory = null)
    {
        optionsMonitor ??= new StaticOptionsMonitor<CoveredCallBacktestOptions>(CreateOptions(dataRoot));

        return new CoveredCallBacktestService(
            _ => new BacktestEngine(
                NullLogger<BacktestEngine>.Instance,
                new StorageCatalogService(dataRoot, new StorageOptions())),
            chainFactory ?? EmptyCoveredCallChainProviderFactory.Instance,
            repository,
            optionsMonitor,
            cache,
            NullLoggerFactory.Instance,
            timeProvider,
            evidenceArtifactStore ?? CreateAuthorityStore());
    }

    private static CoveredCallBacktestOptions CreateOptions(string dataRoot) => new()
    {
        DataRootOverride = dataRoot,
        MaxConcurrentRuns = 1,
        ResultCacheDuration = TimeSpan.FromMinutes(5)
    };

    private static CoveredCallBacktestRequest CreateRequest(
        IReadOnlyList<string>? retainedEvidenceReferences = null) => new(
        UnderlyingSymbol: "SPY",
        From: new DateOnly(2024, 1, 2),
        To: new DateOnly(2024, 1, 3),
        MinStrike: 500m,
        InitialUnderlyingShares: 0,
        Label: "Two-day covered-call retention scenario")
        {
            OperatorAcceptanceCriteria = ["Operator must review the retained covered-call evidence before promotion."],
            RetainedEvidenceReferences = retainedEvidenceReferences ??
            [
                $"evidence://evidence-vault/{DefaultVaultId}"
            ]
        };

    private static CoveredCallBacktestRequest WithEvidenceValues(
        CoveredCallBacktestRequest request,
        string category,
        IReadOnlyList<string> values) =>
        category switch
        {
            nameof(CoveredCallBacktestRequest.OperatorAcceptanceCriteria) =>
                request with { OperatorAcceptanceCriteria = values },
            nameof(CoveredCallBacktestRequest.RetainedEvidenceReferences) =>
                request with { RetainedEvidenceReferences = values },
            nameof(CoveredCallBacktestRequest.AccountingRecordReferences) =>
                request with { AccountingRecordReferences = values },
            nameof(CoveredCallBacktestRequest.ApprovalReferences) =>
                request with { ApprovalReferences = values },
            nameof(CoveredCallBacktestRequest.PaperValidationReferences) =>
                request with { PaperValidationReferences = values },
            nameof(CoveredCallBacktestRequest.GovernedReportReferences) =>
                request with { GovernedReportReferences = values },
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    private static string CreateEvidenceReferenceWithLength(
        string scheme,
        int discriminator,
        int requestedLength)
    {
        var prefix = $"{scheme}://budget-{discriminator}/";
        if (prefix.Length > requestedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedLength));
        }

        return prefix + new string('a', requestedLength - prefix.Length);
    }

    private static IEvidenceArtifactStore CreateAuthorityStore()
    {
        var evidenceStore = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        evidenceStore
            .Setup(store => store.TryOpenManifestByVaultIdAsync(
                DefaultVaultId,
                Scope.TenantId,
                Scope.CompanyId,
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, CancellationToken _) =>
                Task.FromResult<EvidenceManifestFile?>(new EvidenceManifestFile(
                    new MemoryStream(),
                    "application/json",
                    "manifest.json",
                    DateTimeOffset.UtcNow)));
        return evidenceStore.Object;
    }

    private static async Task AssertAuthorityFailureBeforeMutationAsync(
        CoveredCallBacktestRequest request,
        IEvidenceArtifactStore evidenceArtifactStore)
    {
        var repository = new Mock<IStrategyRepository>(MockBehavior.Strict);
        var timeProvider = new CountingTimeProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var service = CreateService(
            "unused-market-data",
            repository.Object,
            cache,
            evidenceArtifactStore,
            timeProvider);

        Func<Task> action = async () => _ = await service.StartAsync(request, Scope);

        await action.Should().ThrowAsync<ArgumentException>();
        timeProvider.UtcNowReadCount.Should().Be(0,
            "authority validation must finish before terminal-run pruning or queue-state creation");
        repository.VerifyNoOtherCalls();
    }

    private static async Task<EvidenceManifestFile?> WaitForCancellationAsync(
        TaskCompletionSource lookupStarted,
        CancellationToken ct)
    {
        lookupStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return null;
    }

    private static async Task<CoveredCallRunStatusDto> WaitForTerminalAsync(
        ICoveredCallBacktestService service,
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct)
    {
        using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (true)
        {
            var status = await service.GetStatusAsync(runId, scope, ct);
            if (status is not null && status.Phase is "Completed" or "Failed" or "Cancelled")
            {
                return status;
            }

            if (!await pollTimer.WaitForNextTickAsync(ct))
            {
                throw new TimeoutException($"Covered-call run '{runId}' did not reach a terminal state.");
            }
        }
    }

    private static async Task<CoveredCallRunStatusDto> WaitForPhaseAsync(
        ICoveredCallBacktestService service,
        string runId,
        CoveredCallRunScope scope,
        string expectedPhase,
        CancellationToken ct)
    {
        using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (true)
        {
            var status = await service.GetStatusAsync(runId, scope, ct);
            if (status?.Phase == expectedPhase)
            {
                return status;
            }

            if (!await pollTimer.WaitForNextTickAsync(ct))
            {
                throw new TimeoutException(
                    $"Covered-call run '{runId}' did not reach phase '{expectedPhase}'.");
            }
        }
    }

    private static void WriteBarJsonl(
        string dataRoot,
        string symbol,
        DateOnly from,
        DateOnly to,
        decimal basePrice)
    {
        var symbolDirectory = Path.Combine(dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDirectory);
        var filePath = Path.Combine(symbolDirectory, $"{symbol}_bars_{from:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var sequence = 1L;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var open = basePrice + date.DayNumber - from.DayNumber;
            var bar = new HistoricalBar(
                Symbol: symbol,
                SessionDate: date,
                Open: open,
                High: open + 2m,
                Low: open - 2m,
                Close: open + 1m,
                Volume: 1_000_000L,
                Source: "covered-call-service-test",
                SequenceNumber: sequence);
            var marketEvent = MarketEvent.HistoricalBar(
                bar.ToTimestampUtc(),
                symbol,
                bar,
                "covered-call-service-test",
                sequence);

            writer.WriteLine(JsonSerializer.Serialize(
                marketEvent,
                MarketDataJsonContext.HighPerformanceOptions));
            sequence++;
        }
    }

    private static string CreateScenarioRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"meridian-covered-call-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteScenarioRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class EmptyCoveredCallChainProviderFactory : ICoveredCallChainProviderFactory
    {
        public static EmptyCoveredCallChainProviderFactory Instance { get; } = new();

        public ValueTask<IOptionChainProvider> CreateAsync(
            string underlyingSymbol,
            DateOnly from,
            DateOnly to,
            int maxDte,
            CancellationToken ct = default) =>
            ValueTask.FromResult<IOptionChainProvider>(EmptyOptionChainProvider.Instance);

        public ValueTask<CoveredCallChainPreviewResult> PreviewAsync(
            string underlyingSymbol,
            DateOnly asOf,
            CancellationToken ct = default) =>
            ValueTask.FromResult(new CoveredCallChainPreviewResult(0m, []));
    }

    private sealed class EmptyOptionChainProvider : IOptionChainProvider
    {
        public static EmptyOptionChainProvider Instance { get; } = new();

        public IReadOnlyList<OptionCandidateInfo> GetCalls(
            string underlyingSymbol,
            DateOnly asOf,
            decimal underlyingPrice) => [];
    }

    private sealed class CancellationBarrierChainProviderFactory : ICoveredCallChainProviderFactory
    {
        public TaskCompletionSource CreateEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IOptionChainProvider> CreateAsync(
            string underlyingSymbol,
            DateOnly from,
            DateOnly to,
            int maxDte,
            CancellationToken ct = default)
        {
            CreateEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return EmptyOptionChainProvider.Instance;
        }

        public ValueTask<CoveredCallChainPreviewResult> PreviewAsync(
            string underlyingSymbol,
            DateOnly asOf,
            CancellationToken ct = default) =>
            ValueTask.FromResult(new CoveredCallChainPreviewResult(0m, []));
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener) => NoOpDisposable.Instance;
    }

    private sealed class ReloadableOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
        where T : class
    {
        private readonly object _gate = new();
        private T _currentValue = currentValue;
        private Action<T, string?>? _listeners;

        public T CurrentValue
        {
            get
            {
                lock (_gate)
                {
                    return _currentValue;
                }
            }
        }

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            lock (_gate)
            {
                _listeners += listener;
            }

            return new CallbackDisposable(() =>
            {
                lock (_gate)
                {
                    _listeners -= listener;
                }
            });
        }

        public void Reload(T value)
        {
            Action<T, string?>? listeners;
            lock (_gate)
            {
                _currentValue = value;
                listeners = _listeners;
            }

            listeners?.Invoke(value, null);
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
    }

    private sealed class ThrowingMemoryCache : IMemoryCache
    {
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            throw new InvalidOperationException("simulated result cache read failure");
        }

        public ICacheEntry CreateEntry(object key) =>
            throw new InvalidOperationException("simulated result cache write failure");

        public void Remove(object key) =>
            throw new InvalidOperationException("simulated result cache removal failure");

        public void Dispose()
        {
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class CountingTimeProvider : TimeProvider
    {
        private int _utcNowReadCount;

        public int UtcNowReadCount => Volatile.Read(ref _utcNowReadCount);

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _utcNowReadCount);
            return new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class RecordingStrategyRepository(
        IStrategyRepository inner,
        bool rejectCompleted = false,
        bool rejectFailed = false,
        bool rejectCancelled = false) : IStrategyRepository
    {
        private readonly ConcurrentQueue<StrategyRunEntry> _attempts = new();
        private readonly ConcurrentQueue<StrategyRunEntry> _successfulAppends = new();
        private readonly ConcurrentQueue<string> _failedAppendMessages = new();

        public IReadOnlyList<StrategyRunEntry> Attempts => [.. _attempts];

        public IReadOnlyList<StrategyRunEntry> SuccessfulAppends => [.. _successfulAppends];

        public IReadOnlyList<string> FailedAppendMessages => [.. _failedAppendMessages];

        public async Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            _attempts.Enqueue(entry);
            if (rejectCompleted && entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Completed)
            {
                Reject(entry, "simulated terminal completion persistence failure");
            }
            if (rejectFailed && entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Failed)
            {
                Reject(entry, "simulated terminal failure persistence failure");
            }
            if (rejectCancelled && entry.LastLifecycleEvent == StrategyRunLifecycleEventType.Cancelled)
            {
                Reject(entry, "simulated terminal cancellation persistence failure");
            }

            try
            {
                await inner.RecordRunAsync(entry, ct);
                _successfulAppends.Enqueue(entry);
            }
            catch (Exception ex)
            {
                _failedAppendMessages.Enqueue($"{entry.LastLifecycleEvent}: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private void Reject(StrategyRunEntry entry, string message)
        {
            var exception = new IOException(message);
            _failedAppendMessages.Enqueue(
                $"{entry.LastLifecycleEvent}: {exception.GetType().Name}: {exception.Message}");
            throw exception;
        }

        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            CancellationToken ct = default) =>
            inner.GetRunsAsync(strategyId, ct);

        public Task<StrategyRunEntry?> GetLatestRunAsync(
            string strategyId,
            CancellationToken ct = default) =>
            inner.GetLatestRunAsync(strategyId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) =>
            inner.GetAllRunsAsync(ct);

        public Task<StrategyRunEntry?> GetRunByIdAsync(
            string runId,
            CancellationToken ct = default) =>
            inner.GetRunByIdAsync(runId, ct);

        public Task<IReadOnlyList<StrategyRunEntry>> GetRunsByIdsAsync(
            IReadOnlyCollection<string> runIds,
            CancellationToken ct = default) =>
            inner.GetRunsByIdsAsync(runIds, ct);

        public Task<IReadOnlyList<StrategyRunEntry>> QueryRunsAsync(
            StrategyRunRepositoryQuery query,
            CancellationToken ct = default) =>
            inner.QueryRunsAsync(query, ct);
    }
}
