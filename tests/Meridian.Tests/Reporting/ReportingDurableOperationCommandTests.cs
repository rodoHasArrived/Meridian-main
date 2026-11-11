using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Tests.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingDurableOperationCommandTests(ReportingArtifactDatabaseFixture database)
    : IClassFixture<ReportingArtifactDatabaseFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReportAccessQueryContext Access = new("operator", CompanyId: "company-a",
        TenantId: "tenant-a", RequireBoundScope: true);
    private static readonly ReportingGovernanceCallerContext Caller = new("operator", "tenant-a", "company-a",
        UserPermission.ManageReporting, ReportingCommandOrigin.HumanOperator, "notice-recovery-test");

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task Replay_WithCurrentSourcesUnavailable_ReturnsFrozenReleasedRunAndDisablesSubmission()
    {
        var (store, manifest, operation) = await SeedAsync();
        var released = Governed(manifest, GovernedReportingState.Released);
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>()).Returns(released);
        coordinator.ListRetainedArtifactsAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReportingGovernedArtifactDescriptor>());
        var harness = Command(store, coordinator);

        var result = await harness.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller);

        result.Run.RunId.Should().Be(manifest.RunId);
        result.Run.Status.Should().Be(nameof(GovernedReportingState.Released));
        result.Run.RunAttemptOrdinal.Should().Be(1);
        result.Run.ResolvedTemplate.Should().BeEquivalentTo(manifest.ResolvedTemplate);
        result.Run.ResolvedParameters.Should().BeEquivalentTo(manifest.ResolvedParameters);
        result.Run.Readiness.Should().BeEquivalentTo(manifest.Readiness);
        result.Run.SectionCount.Should().Be(manifest.Sections.Length);
        result.Run.NextActions.Should().ContainSingle(action => action.Kind == "approval-submit"
            && !action.IsEnabled && action.DisabledReason!.Contains("advanced beyond Draft"));
        await coordinator.Received(1).ListRetainedArtifactsAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>());
        await coordinator.DidNotReceive().CreateFromCompletedCertifiedManifestAsync(
            Arg.Any<string>(), Arg.Any<ReportingGovernanceCallerContext>(), Arg.Any<CancellationToken>());
        AssertCurrentInputsUnused(harness);
        store.GetManifest("tenant-a", manifest.RunId).Should().BeEquivalentTo(manifest);
        store.ListRuns("tenant-a", "company-a").Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task Replay_WithoutCurrentManagementPermission_RefusesBeforeGovernanceOrSourceReads()
    {
        var (store, manifest, operation) = await SeedAsync();
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        var harness = Command(store, coordinator);
        var readOnlyCaller = Caller with { Permissions = UserPermission.ViewReporting };

        var replay = () => harness.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, readOnlyCaller);

        await replay.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*current reporting management permission*");
        coordinator.ReceivedCalls().Should().BeEmpty();
        AssertCurrentInputsUnused(harness);
        store.GetManifest("tenant-a", manifest.RunId).Should().BeEquivalentTo(manifest);
    }

    [ReportingDatabaseFact]
    public async Task Replay_WhenCanonicalGovernanceDeniesCurrentAccess_DoesNotReturnRetainedRun()
    {
        var (store, manifest, operation) = await SeedAsync();
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GovernedReportingRun>(
                new ReportingGovernanceAuthorizationException("Current reporting audience access was revoked.")));
        var harness = Command(store, coordinator);

        var replay = () => harness.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller);

        await replay.Should().ThrowAsync<ReportingGovernanceAuthorizationException>().WithMessage("*access was revoked*");
        await coordinator.DidNotReceive().ListRetainedArtifactsAsync(
            Arg.Any<string>(), Arg.Any<ReportingGovernanceCallerContext>(), Arg.Any<CancellationToken>());
        await coordinator.DidNotReceive().CreateFromCompletedCertifiedManifestAsync(
            Arg.Any<string>(), Arg.Any<ReportingGovernanceCallerContext>(), Arg.Any<CancellationToken>());
        AssertCurrentInputsUnused(harness);
    }

    [ReportingDatabaseFact]
    public async Task Replay_WithChangedRequestHash_RefusesBeforeGovernanceAndPreservesOriginal()
    {
        var (store, manifest, operation) = await SeedAsync();
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        var harness = Command(store, coordinator);
        var changed = operation with { RequestHash = Sha256Digest.ComputeUtf8("different-notice-request") };

        var replay = () => harness.Service.RunOperationAsync(Request(manifest), changed, "operator", Access, Caller);

        await replay.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*different request or authority scope*");
        coordinator.ReceivedCalls().Should().BeEmpty();
        AssertCurrentInputsUnused(harness);
        store.GetManifest("tenant-a", manifest.RunId).Should().BeEquivalentTo(manifest);
        store.GetAudit("tenant-a", manifest.RunId).Select(entry => entry.Action).Should().Equal("RunGenerated");
    }

    [ReportingDatabaseFact]
    public async Task GovernanceCommitAcknowledgementLost_RestartRecoversSameFrozenRunWithoutCurrentSource()
    {
        var (store, manifest, operation) = await SeedAsync();
        var original = Governed(manifest, GovernedReportingState.Draft);
        GovernedReportingRun? retained = null;
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>()).Returns(_ => retained is null
            ? Task.FromException<GovernedReportingRun>(new ReportingGovernanceNotFoundException("Not yet created."))
            : Task.FromResult(retained));
        coordinator.CreateFromCompletedCertifiedManifestAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // Model a durable governance/artifact commit whose acknowledgement was lost.
                retained = original;
                return Task.FromException<GovernedReportingRun>(new IOException("Lost governance commit acknowledgement."));
            });
        coordinator.ListRetainedArtifactsAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReportingGovernedArtifactDescriptor>());
        var first = Command(store, coordinator);

        var interrupted = () => first.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller);
        await interrupted.Should().ThrowAsync<IOException>().WithMessage("*Lost governance commit acknowledgement*");
        retained.Should().BeSameAs(original);
        AssertCurrentInputsUnused(first);

        var restarted = Command(new PostgresReportingRunStore(database.Options), coordinator);
        var recovered = await restarted.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller);

        recovered.Run.RunId.Should().Be(manifest.RunId);
        recovered.Run.Status.Should().Be(nameof(GovernedReportingState.Draft));
        recovered.Run.ResolvedParameters.Should().BeEquivalentTo(manifest.ResolvedParameters);
        recovered.Run.Readiness.Should().BeEquivalentTo(manifest.Readiness);
        recovered.Run.RunAttemptOrdinal.Should().Be(1);
        await coordinator.Received(1).CreateFromCompletedCertifiedManifestAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>());
        await coordinator.Received(1).ListRetainedArtifactsAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>());
        AssertCurrentInputsUnused(restarted);
        store.GetManifest("tenant-a", manifest.RunId).Should().BeEquivalentTo(manifest);
        store.ListRuns("tenant-a", "company-a").Should().ContainSingle();
        store.GetAudit("tenant-a", manifest.RunId).Select(entry => entry.Action).Should().Equal("RunGenerated");
    }

    [ReportingDatabaseFact]
    public async Task FailedOperation_ReplaySignalsExactTerminalRunOnlyAfterCurrentAccessAndRequestBinding()
    {
        var id = $"notice-command-failed-{Guid.NewGuid():N}";
        var contract = ReportingDurableOperationTests.DurableContract(id,
            accessMode: ReportingGovernanceAccessMode.Restricted);
        var store = new PostgresReportingRunStore(database.Options);
        var renderer = new FailingRenderer();
        var host = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), renderer, () => Now, store);
        var generate = () => host.ExecuteAsync(contract, CancellationToken.None);
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("Reporting run failed after 1 attempts.");
        var failed = store.GetManifest("tenant-a", id);
        failed.Should().NotBeNull();
        failed!.Status.Should().Be(ReportingRunStatus.Failed);
        failed.FailureReason.Should().Be("Injected terminal notice rendering failure.");
        failed.Sections.Should().BeEmpty();
        failed.Artifacts.Should().BeEmpty();
        failed.RunAttemptOrdinal.Should().Be(1);
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        var replay = Command(new PostgresReportingRunStore(database.Options), coordinator);
        var operation = new ReportingRunOperation(id, contract.OperationRequestHash!);

        var terminal = await Record.ExceptionAsync(() => replay.Service.RunOperationAsync(
            Request(failed), operation, "operator", Access, Caller));
        terminal.Should().BeOfType<ReportingNoticeOperationFailedException>().Which.RunId.Should().Be(id);

        var changedRequest = await Record.ExceptionAsync(() => replay.Service.RunOperationAsync(Request(failed),
            operation with { RequestHash = Sha256Digest.ComputeUtf8("changed-failed-notice-request") }, "operator", Access, Caller));
        changedRequest.Should().BeOfType<ReportingGovernanceException>()
            .Which.Message.Should().Contain("different request or authority scope");
        var missingPermission = await Record.ExceptionAsync(() => replay.Service.RunOperationAsync(Request(failed), operation,
            "operator", Access, Caller with { Permissions = UserPermission.ViewReporting }));
        missingPermission.Should().BeOfType<ReportingGovernanceAuthorizationException>();
        var deniedAudience = await Record.ExceptionAsync(() => replay.Service.RunOperationAsync(Request(failed), operation,
            "other-operator", Access with { ActorPrincipalId = "other-operator" }, Caller with { ActorId = "other-operator" }));
        deniedAudience.Should().BeOfType<ReportingGovernanceAuthorizationException>();

        coordinator.ReceivedCalls().Should().BeEmpty();
        AssertCurrentInputsUnused(replay);
        renderer.RenderCount.Should().Be(1);
        store.GetManifest("tenant-a", id).Should().BeEquivalentTo(failed);
        store.ListRuns("tenant-a", "company-a").Should().ContainSingle();
        store.GetAudit("tenant-a", id).Select(entry => entry.Action).Should().Equal("RunFailed");
        store.GetManifest("tenant-a", id + "-v2").Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task TwoCommandHosts_RacingToResumeGovernance_ReturnTheSameCanonicalDraft()
    {
        var (store, manifest, operation) = await SeedAsync();
        var draft = Governed(manifest, GovernedReportingState.Draft);
        var running = draft with { ExecutionState = GovernedReportingExecutionState.Running };
        var initialReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var createState = 0;
        var createCommits = 0;
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>()).Returns(_ => ReadAsync());
        coordinator.CreateFromCompletedCertifiedManifestAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(_ => CreateAsync());
        coordinator.ListRetainedArtifactsAsync(manifest.RunId, Caller, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReportingGovernedArtifactDescriptor>());
        var first = Command(store, coordinator);
        var second = Command(new PostgresReportingRunStore(database.Options), coordinator);
        var workers = Task.WhenAll(
            first.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller),
            second.Service.RunOperationAsync(Request(manifest), operation, "operator", Access, Caller));

        try
        {
            await duplicateCreate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            workers.IsCompleted.Should().BeFalse();
            finishCreate.TrySetResult();
            var results = await workers.WaitAsync(TimeSpan.FromSeconds(15));

            results[0].Should().BeEquivalentTo(results[1]);
            results.Should().OnlyContain(result => result.Run.RunId == manifest.RunId
                && result.Run.Status == nameof(GovernedReportingState.Draft) && result.Run.RunAttemptOrdinal == 1);
            results.Should().OnlyContain(result => result.Run.NextActions!.All(action => action.IsEnabled));
            createCommits.Should().Be(1);
            AssertCurrentInputsUnused(first);
            AssertCurrentInputsUnused(second);
            store.ListRuns("tenant-a", "company-a").Should().ContainSingle();
        }
        finally
        {
            finishCreate.TrySetResult();
            try
            { await workers.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch { }
        }

        async Task<GovernedReportingRun> ReadAsync()
        {
            // Both hosts observed absent governance before either attempted creation.
            if (Interlocked.Increment(ref reads) <= 2)
            {
                if (Volatile.Read(ref reads) == 2)
                    initialReads.TrySetResult();
                await initialReads.Task;
                throw new ReportingGovernanceNotFoundException("Governance has not yet been retained.");
            }
            return Volatile.Read(ref createState) == 2 ? draft : running;
        }

        async Task<GovernedReportingRun> CreateAsync()
        {
            if (Interlocked.CompareExchange(ref createState, 1, 0) != 0)
            {
                duplicateCreate.TrySetResult();
                throw new ReportingGovernanceException($"Reporting run '{manifest.RunId}' already exists.");
            }
            await finishCreate.Task;
            Interlocked.Increment(ref createCommits);
            Volatile.Write(ref createState, 2);
            return draft;
        }
    }

    private async Task<(PostgresReportingRunStore Store, ReportingOutputManifest Manifest, ReportingRunOperation Operation)> SeedAsync()
    {
        var id = $"notice-command-{Guid.NewGuid():N}";
        var contract = ReportingDurableOperationTests.DurableContract(id);
        var store = new PostgresReportingRunStore(database.Options);
        var host = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(), () => Now, store);
        var manifest = await host.ExecuteAsync(contract, CancellationToken.None);
        ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest).Should().BeTrue();
        return (store, manifest, new(id, contract.OperationRequestHash!));
    }

    private static ReportingRunRequestDto Request(ReportingOutputManifest manifest) =>
        new(manifest.TemplateId, Parameters: manifest.ResolvedParameters);

    private static GovernedReportingRun Governed(ReportingOutputManifest manifest, GovernedReportingState state)
    {
        var authority = new ReportingAuthorityScope("operator", "tenant-a", manifest.OperationalScope!.OrganizationId,
            "company-a", [ReportingGovernancePermission.CreateRun, ReportingGovernancePermission.ExecuteRun],
            ReportingCommandOrigin.HumanOperator, "original-run");
        return new(manifest.RunId, manifest.RunSeriesId!, 1, manifest.TemplateId, "1", manifest.OperationalScope,
            manifest.ImmutableAccessScope!, manifest.CertifiedSnapshot!, authority, Now, null,
            GovernedReportingExecutionState.Succeeded, state, state == GovernedReportingState.Released ? 7 : 3,
            null, state == GovernedReportingState.Released ? new(authority, Now.AddMinutes(1), "Approved notice") : null,
            state == GovernedReportingState.Released ? new(authority, Now.AddMinutes(2), "manifest-id", new string('f', 64),
                ImmutableArray<ReportingArtifactReference>.Empty, ["retained-release-evidence"]) : null,
            ImmutableArray<ReportingGovernanceAuditEntry>.Empty);
    }

    private static CommandHarness Command(IReportingRunStore store, IReportingGovernanceEndpointCoordinator coordinator)
    {
        var catalog = new UnavailableCatalog();
        var source = new UnavailableSource();
        var orchestration = Substitute.For<IReportingOrchestrationService>();
        var service = new ReportingRunCommandService(orchestration, catalog,
            certificationService: new ReportingRunCertificationService(source,
                Substitute.For<IReportingReconciliationEvidenceSource>()),
            governanceCoordinator: coordinator, runStore: store);
        return new(service, catalog, source, orchestration);
    }

    private static void AssertCurrentInputsUnused(CommandHarness harness)
    {
        harness.Catalog.Lookups.Should().Be(0);
        harness.Source.Captures.Should().Be(0);
        harness.Orchestration.ReceivedCalls().Should().BeEmpty();
    }

    private sealed record CommandHarness(ReportingRunCommandService Service, UnavailableCatalog Catalog,
        UnavailableSource Source, IReportingOrchestrationService Orchestration);

    private sealed class UnavailableCatalog : IReportingTemplateCatalog
    {
        public int Lookups { get; private set; }
        public IReadOnlyList<ReportingTemplateMetadata> ListTemplates()
        {
            Lookups++;
            throw new InvalidOperationException("The current reporting catalog is unavailable.");
        }

        public ReportingTemplateMetadata Get(string templateId)
        {
            Lookups++;
            throw new KeyNotFoundException("The current catalog no longer exposes the original template.");
        }
    }

    private sealed class UnavailableSource : IReportingAuthoritativeSource
    {
        public int Captures { get; private set; }
        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext, CancellationToken cancellationToken = default)
        {
            Captures++;
            return ValueTask.FromException<ReportingAuthoritativeSourceCapture>(
                new ReportingAuthoritativeSourceUnavailableException("The current ledger source is unavailable."));
        }
    }

    private sealed class FailingRenderer : IReportingSectionRenderer
    {
        public int RenderCount { get; private set; }
        public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract,
            ReportingTemplateMetadata template, string sectionId, int attempt)
        {
            RenderCount++;
            throw new InvalidOperationException("Injected terminal notice rendering failure.");
        }
    }
}
