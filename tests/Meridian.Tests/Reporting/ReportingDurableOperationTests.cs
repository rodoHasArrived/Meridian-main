using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Tests.Storage.Reporting;
using Meridian.Tests.Ui;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingDurableOperationTests(ReportingArtifactDatabaseFixture database)
    : IClassFixture<ReportingArtifactDatabaseFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset FirstTime = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task LostCreateAcknowledgement_RestartRecoversOriginalManifestWithoutRecertifyingTimestamps()
    {
        var operationId = $"notice-operation-{Guid.NewGuid():N}";
        var contract = DurableContract(operationId);
        var durable = new PostgresReportingRunStore(database.Options);
        var probe = new RunStoreProbe(durable, throwAfterCreate: true);
        var firstRenderer = new ProbeRenderer();
        var first = Host(probe, firstRenderer, FirstTime);

        var acknowledgementFailure = await Record.ExceptionAsync(() => first.ExecuteAsync(contract, CancellationToken.None));

        if (acknowledgementFailure is not null)
            acknowledgementFailure.Should().BeOfType<IOException>();
        probe.CreateCommitCount.Should().Be(1);
        probe.AcknowledgementFailureCount.Should().Be(1);
        var committed = durable.GetManifest("tenant-a", operationId);
        committed.Should().NotBeNull();
        committed!.Status.Should().Be(ReportingRunStatus.Draft);

        var replayContract = contract with
        {
            RequestedAtUtc = FirstTime.AddHours(1),
            Readiness = contract.Readiness! with
            {
                EvaluationId = "new-evaluation-after-restart",
                EvaluatedAtUtc = FirstTime.AddHours(1)
            },
            CertifiedSnapshot = contract.CertifiedSnapshot! with { CapturedAtUtc = FirstTime.AddHours(1) },
            AuthoritativeSource = contract.AuthoritativeSource! with { CapturedAtUtc = FirstTime.AddHours(1) }
        };
        var reloadedStore = new PostgresReportingRunStore(database.Options);
        var replayRenderer = new ProbeRenderer();
        var replay = await Host(reloadedStore, replayRenderer, FirstTime.AddHours(1))
            .ExecuteAsync(replayContract, CancellationToken.None);

        replay.Should().BeEquivalentTo(committed);
        replay.RunId.Should().Be(operationId);
        replay.RunAttemptOrdinal.Should().Be(1);
        replay.CertifiedSnapshot!.CapturedAtUtc.Should().Be(contract.CertifiedSnapshot!.CapturedAtUtc);
        replay.AuthoritativeSource!.CapturedAtUtc.Should().Be(contract.AuthoritativeSource!.CapturedAtUtc);
        replayRenderer.RenderCount.Should().Be(0);
        reloadedStore.ListRuns("tenant-a", "company-a").Should().ContainSingle();
        reloadedStore.GetAudit("tenant-a", operationId).Select(entry => entry.Action).Should().Equal("RunGenerated");
        reloadedStore.GetManifest("tenant-a", operationId + "-v2").Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task ReusedOperationId_WithChangedSemanticHash_RefusesWithoutReplacingWinner()
    {
        var operationId = $"notice-operation-{Guid.NewGuid():N}";
        var store = new PostgresReportingRunStore(database.Options);
        var original = await Host(store, new ProbeRenderer(), FirstTime)
            .ExecuteAsync(DurableContract(operationId), CancellationToken.None);
        var conflicting = DurableContract(operationId, Sha256Digest.ComputeUtf8("changed-notice-request"));
        var renderer = new ProbeRenderer();
        var restarted = Host(new PostgresReportingRunStore(database.Options), renderer, FirstTime.AddHours(1));

        var retry = () => restarted.ExecuteAsync(conflicting, CancellationToken.None);

        await retry.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*different request or authority scope*");
        renderer.RenderCount.Should().Be(0);
        store.GetManifest("tenant-a", operationId).Should().BeEquivalentTo(original);
        store.ListRuns("tenant-a", "company-a").Should().ContainSingle();
        store.GetAudit("tenant-a", operationId).Select(entry => entry.Action).Should().Equal("RunGenerated");
    }

    [ReportingDatabaseFact]
    public async Task TwoIndependentHosts_OneDurableOperation_RendersAndCommitsOnlyOneVersion()
    {
        var operationId = $"notice-operation-{Guid.NewGuid():N}";
        var contract = DurableContract(operationId);
        var firstRenderer = new ProbeRenderer(blockFirstRender: true);
        var secondRenderer = new ProbeRenderer();
        var firstStore = new PostgresReportingRunStore(database.Options);
        var secondStore = new RunStoreProbe(new PostgresReportingRunStore(database.Options));
        var firstHost = Host(firstStore, firstRenderer, FirstTime);
        var secondHost = Host(secondStore, secondRenderer, FirstTime.AddMinutes(1));
        var first = Task.Run(() => firstHost.ExecuteAsync(contract, CancellationToken.None));
        Task<ReportingOutputManifest>? second = null;

        try
        {
            await firstRenderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = Task.Run(() => secondHost.ExecuteAsync(contract, CancellationToken.None));
            await secondStore.BlockedClaim.Task.WaitAsync(TimeSpan.FromSeconds(10));
            secondRenderer.RenderCount.Should().Be(0);
            second.IsCompleted.Should().BeFalse();
            firstRenderer.Release.TrySetResult();

            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));

            results[1].Should().BeEquivalentTo(results[0]);
            results.Should().OnlyContain(run => run.RunId == operationId && run.RunAttemptOrdinal == 1);
            firstRenderer.RenderCount.Should().Be(results[0].Sections.Length);
            secondRenderer.RenderCount.Should().Be(0);
            firstStore.ListRuns("tenant-a", "company-a").Should().ContainSingle();
            firstStore.GetAudit("tenant-a", operationId).Select(entry => entry.Action).Should().Equal("RunGenerated");
        }
        finally
        {
            firstRenderer.Release.TrySetResult();
            // Finish any started worker before the fixture drops its private schema.
            try
            { await first.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch { }
            if (second is not null)
                try
                { await second.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch { }
        }
    }

    internal static ReportingJobContract DurableContract(string operationId, string? requestHash = null,
        ReportingGovernanceAccessMode accessMode = ReportingGovernanceAccessMode.CompanyWide)
    {
        requestHash ??= Sha256Digest.ComputeUtf8("same-notice-request");
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [ReportingOperationIdentity.OperationIdParameter] = operationId,
            [ReportingOperationIdentity.RequestHashParameter] = requestHash
        };
        return ReportingOperationalConcurrencyTests.BuildCertifiedContract(operationId, parameters, accessMode) with
        {
            DurableOperationId = operationId,
            OperationRequestHash = requestHash
        };
    }

    private static ReportingOrchestrationService Host(IReportingRunStore store, IReportingSectionRenderer renderer,
        DateTimeOffset now) => new(new DefaultReportingTemplateCatalog(), renderer, () => now, store);

    private sealed class ProbeRenderer(bool blockFirstRender = false) : IReportingSectionRenderer
    {
        private readonly DeterministicReportingSectionRenderer _inner = new();
        private int _renderCount;
        public int RenderCount => Volatile.Read(ref _renderCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract,
            ReportingTemplateMetadata template, string sectionId, int attempt)
        {
            if (Interlocked.Increment(ref _renderCount) == 1 && blockFirstRender)
            {
                Entered.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
            }
            return _inner.RenderSection(runId, contract, template, sectionId, attempt);
        }
    }

    private sealed class RunStoreProbe(IReportingRunStore inner, bool throwAfterCreate = false) : IReportingRunStore
    {
        private int _throwAfterCreate = throwAfterCreate ? 1 : 0;
        private int _createCommitCount;
        private int _acknowledgementFailureCount;
        public bool SupportsDurableCreateClaims => inner.SupportsDurableCreateClaims;
        public int CreateCommitCount => Volatile.Read(ref _createCommitCount);
        public int AcknowledgementFailureCount => Volatile.Read(ref _acknowledgementFailureCount);
        public TaskCompletionSource BlockedClaim { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) => inner.ListRuns(limit);
        public ReportingOutputManifest? GetManifest(string runId) => inner.GetManifest(runId);
        public ReportingOutputManifest? GetManifest(string tenantId, string runId) => inner.GetManifest(tenantId, runId);
        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) => inner.GetAudit(runId);
        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string tenantId, string runId) => inner.GetAudit(tenantId, runId);
        public string? GetRevision(string runId) => inner.GetRevision(runId);
        public string? GetRevision(string tenantId, string runId) => inner.GetRevision(tenantId, runId);
        public Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            CancellationToken ct = default) => inner.SaveAsync(manifest, auditTrail, ct);
        public Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            string? expectedRevision, CancellationToken ct = default) => inner.SaveAsync(manifest, auditTrail, expectedRevision, ct);
        public async Task<ReportingRunCreateClaimResult> TryClaimCreateAsync(string tenantId, string runId,
            string leaseOwner, DateTimeOffset nowUtc, TimeSpan leaseDuration, CancellationToken ct = default)
        {
            var result = await inner.TryClaimCreateAsync(tenantId, runId, leaseOwner, nowUtc, leaseDuration, ct);
            if (result.Status == ReportingRunCreateClaimStatus.LeasedByAnotherOwner)
                BlockedClaim.TrySetResult();
            return result;
        }
        public Task<bool> RenewCreateClaimAsync(string tenantId, string runId, string leaseOwner,
            long leaseVersion, TimeSpan leaseDuration, CancellationToken ct = default)
            => inner.RenewCreateClaimAsync(tenantId, runId, leaseOwner, leaseVersion, leaseDuration, ct);
        public Task ReleaseCreateClaimAsync(string tenantId, string runId, string leaseOwner, long leaseVersion,
            CancellationToken ct = default) => inner.ReleaseCreateClaimAsync(tenantId, runId, leaseOwner, leaseVersion, ct);
        public async Task SaveClaimedCreateAsync(ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail, string leaseOwner, long leaseVersion, CancellationToken ct = default)
        {
            await inner.SaveClaimedCreateAsync(manifest, auditTrail, leaseOwner, leaseVersion, ct);
            Interlocked.Increment(ref _createCommitCount);
            if (Interlocked.Exchange(ref _throwAfterCreate, 0) == 1)
            {
                Interlocked.Increment(ref _acknowledgementFailureCount);
                throw new IOException("Injected lost acknowledgement after durable reporting create.");
            }
        }
    }
}

public sealed class ReportingDurableOperationCapabilityTests
{
    [Fact]
    public async Task StableOperation_RefusesProcessLocalFileClaimsBeforeRendering()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileReportingRunStore(new(root), NullLogger<FileReportingRunStore>.Instance);
            var host = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(),
                new DeterministicReportingSectionRenderer(), () => DateTimeOffset.UtcNow, store);

            var create = () => host.ExecuteAsync(
                ReportingDurableOperationTests.DurableContract("notice-operation-requires-durable-claims"), CancellationToken.None);

            await create.Should().ThrowAsync<ReportingRunCreateClaimException>().WithMessage("*cross-process durable create claims*");
            store.ListRuns().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
