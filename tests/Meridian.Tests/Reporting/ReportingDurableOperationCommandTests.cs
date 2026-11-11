using System.Collections.Immutable;
using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Tests.Storage.Reporting;
using Meridian.Tests.Ui;
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
    public async Task SelectedCommitmentNotices_RetainSeparateExactRuns_RefuseRetargetAndRecoverLegacyOmission()
    {
        var fixture = CapitalCallNoticeReportingTests.MultipleCommitments();
        var legacyFixture = CapitalCallNoticeReportingTests.MultipleCommitments(singleLegacyCommitment: true);
        var firstIntent = Guid.NewGuid();
        var firstOperation = CapitalCallNoticeReportingService.BuildOperation(fixture.Scope, fixture.Call.CallId,
            "investor-a", firstIntent, "operator", fixture.Call.Lines[0].CommitmentId);
        var secondOperation = CapitalCallNoticeReportingService.BuildOperation(fixture.Scope, fixture.Call.CallId,
            "investor-a", Guid.NewGuid(), "operator", fixture.Call.Lines[1].CommitmentId);
        var legacyOperation = CapitalCallNoticeReportingService.BuildOperation(legacyFixture.Scope, legacyFixture.Call.CallId,
            "investor-a", Guid.NewGuid(), "operator");
        var store = new PostgresReportingRunStore(database.Options);
        var host = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(), () => DateTimeOffset.UtcNow, store);
        var cases = new[]
        {
            (Fixture: fixture, Line: fixture.Call.Lines[0], Operation: firstOperation, Legacy: false),
            (Fixture: fixture, Line: fixture.Call.Lines[1], Operation: secondOperation, Legacy: false),
            (Fixture: legacyFixture, Line: legacyFixture.Call.Lines[0], Operation: legacyOperation, Legacy: true)
        };
        var retained = new Dictionary<string, ReportingOutputManifest>(StringComparer.Ordinal);
        foreach (var selected in cases)
        {
            var certified = await CapitalCallNoticeReportingTests.CertifiedSelectionAsync(
                selected.Fixture, selected.Line, selected.Operation, selected.Legacy);
            var manifest = await host.ExecuteAsync(certified.Contract, CancellationToken.None);
            ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest).Should().BeTrue();
            manifest.ResolvedParameters!.AccountingBasis.Should().Be(ReportingAccountingBasisDto.Management);
            manifest.AuthoritativeSource!.AccountingBasis.Should().Be("Primary");
            ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest with
            { AuthoritativeSource = manifest.AuthoritativeSource with { AccountingBasis = "Gaap" } }).Should().BeFalse();
            manifest.CertifiedDatasetRows.Should().HaveCount(2);
            manifest.CertifiedDatasetRows.Should().OnlyContain(row => row["journalEntryId"] == selected.Line.IssuanceJournalEntryId!.Value.ToString("D")
                && row["externalGl.commitmentId"] == selected.Line.CommitmentId);
            manifest.CertifiedDatasetRows.Sum(row => decimal.Parse(row["debit"], CultureInfo.InvariantCulture))
                .Should().Be(selected.Line.Amount);
            var model = DocumentsReportingPrimaryDocumentRenderer.BuildModel(manifest);
            model.HeaderFields.Should().Contain(field => field.Label == "Commitment" && field.Value == selected.Line.CommitmentId);
            model.HeaderFields.Should().Contain(field => field.Label == "Posted issuance journal"
                && field.Value == selected.Line.IssuanceJournalEntryId!.Value.ToString("D"));
            model.Tables.Single().Rows.Should().ContainSingle(row => row[0] == "Amount due"
                && row[1] == selected.Line.Amount.ToString("G29", CultureInfo.InvariantCulture));
            retained.Add(manifest.RunId, manifest);
        }

        var governed = retained.Values.ToDictionary(manifest => manifest.RunId,
            manifest => Governed(manifest, GovernedReportingState.Draft), StringComparer.Ordinal);
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync(Arg.Any<string>(), Caller, Arg.Any<CancellationToken>())
            .Returns(call => governed[call.ArgAt<string>(0)]);
        coordinator.ListRetainedArtifactsAsync(Arg.Any<string>(), Caller, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReportingGovernedArtifactDescriptor>());
        var restarted = Command(new PostgresReportingRunStore(database.Options), coordinator);
        foreach (var selected in cases)
        {
            var original = retained[selected.Operation.RunId];
            var replay = await restarted.Service.RunOperationAsync(Request(original), selected.Operation, "operator", Access, Caller);
            replay.Run.RunId.Should().Be(original.RunId);
            replay.Run.ResolvedParameters.Should().BeEquivalentTo(original.ResolvedParameters);
            store.GetManifest("tenant-a", original.RunId).Should().BeEquivalentTo(original);
            store.GetAudit("tenant-a", original.RunId).Select(entry => entry.Action).Should().Equal("RunGenerated");
        }
        var retarget = CapitalCallNoticeReportingService.BuildOperation(fixture.Scope, fixture.Call.CallId,
            "investor-a", firstIntent, "operator", fixture.Call.Lines[1].CommitmentId);
        retarget.RunId.Should().Be(firstOperation.RunId);
        var collision = () => restarted.Service.RunOperationAsync(Request(retained[firstOperation.RunId]), retarget,
            "operator", Access, Caller);
        await collision.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*different request or authority scope*");
        AssertCurrentInputsUnused(restarted);
        await coordinator.DidNotReceive().CreateFromCompletedCertifiedManifestAsync(
            Arg.Any<string>(), Arg.Any<ReportingGovernanceCallerContext>(), Arg.Any<CancellationToken>());
        store.ListRuns("tenant-a", "company-a").Should().HaveCount(3);

        var summaries = await new CapitalCallNoticeReportingService(fixture.Validator, null!, restarted.Service,
            runStore: store, governance: new SummaryGovernanceRepository(governed))
            .ReadNoticeSummariesAsync(fixture.Scope, Access);
        summaries.Should().HaveCount(3);
        foreach (var selected in cases)
            summaries.Should().ContainSingle(summary => summary.RunId == selected.Operation.RunId
                && summary.CommitmentId == selected.Line.CommitmentId && summary.CallId == selected.Fixture.Call.CallId
                && summary.InvestorId == "investor-a");
    }

    [ReportingDatabaseFact]
    public async Task HistoricalRawParameterManifest_PostgresPreservesFrozenBytesAndRejectsReboundTampering()
    {
        var fixture = CapitalCallNoticeReportingTests.MultipleCommitments(singleLegacyCommitment: true);
        var operation = CapitalCallNoticeReportingService.BuildOperation(fixture.Scope, fixture.Call.CallId,
            "investor-a", Guid.NewGuid(), "operator");
        var certified = await CapitalCallNoticeReportingTests.CertifiedSelectionAsync(
            fixture, fixture.Call.Lines[0], operation, legacyFilter: true);
        var contract = certified.Contract;
        var parameters = contract.ResolvedParameters!;
        parameters = parameters with
        {
            Scope = parameters.Scope with
            {
                Dimensions = parameters.Scope.Dimensions! with
                {
                    ExternalGlDimensions = new Dictionary<string, string>
                    { ["capitalCallId"] = fixture.Call.CallId, ["capitalActivityKind"] = "CapitalCallIssuance" }
                }
            }
        };
        const string sortedMap = "\"capitalActivityKind\":\"CapitalCallIssuance\",\"capitalCallId\":\"legacy-call\"";
        const string rawMap = "\"capitalCallId\":\"legacy-call\",\"capitalActivityKind\":\"CapitalCallIssuance\"";
        var frozenRaw = contract.CertifiedSnapshot!.ParametersCanonicalJson!.Replace(sortedMap, rawMap, StringComparison.Ordinal);
        frozenRaw.Should().NotBe(contract.CertifiedSnapshot.ParametersCanonicalJson);
        var frozenHash = Sha256Digest.ComputeUtf8(frozenRaw);
        var snapshot = contract.CertifiedSnapshot with { ParametersCanonicalJson = frozenRaw, ParametersHash = frozenHash };
        var readiness = contract.Readiness! with { ResolvedParameters = parameters };
        var template = new DefaultReportingTemplateCatalog().Get(contract.TemplateId);
        var manifest = new ReportingOutputManifest(operation.RunId, contract.TemplateId, contract.AsOfDate,
            ReportingRunStatus.Draft, [], ReportingArtifactDeclaration.Build(operation.RunId, template, parameters,
                includeCertifiedSourceSchedule: true).Select(artifact => artifact.ArtifactId).ToImmutableArray(),
            1, ReportingRunTrigger.AdHoc, ReportWriterGrids: [], RenderedReportWriterGrids: [],
            ReportWriterGridDiffs: [], RunSeriesId: operation.RunId, RunAttemptOrdinal: 1,
            ResolvedTemplate: contract.ResolvedTemplate, ResolvedParameters: parameters, Readiness: readiness,
            OperationalScope: contract.OperationalScope, ImmutableAccessScope: contract.ImmutableAccessScope,
            CertifiedSnapshot: snapshot, AuthoritativeSource: contract.AuthoritativeSource,
            CertifiedDatasetRows: contract.DatasetRows!.ToImmutableArray());
        var snapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest);
        snapshot = snapshot with { SnapshotHash = snapshotHash, SnapshotId = "report-snapshot-" + snapshotHash[..32] };
        contract = contract with { ResolvedParameters = parameters, Readiness = readiness, CertifiedSnapshot = snapshot };
        var renderer = new DeterministicReportingSectionRenderer();
        manifest = manifest with
        {
            CertifiedSnapshot = snapshot,
            Sections = template.Sections
            .Select(section => renderer.RenderSection(operation.RunId, contract, template, section, 1)).ToImmutableArray()
        };
        ReportingCertifiedManifestValidation.Validate(manifest);
        ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest).Should().BeTrue();
        var audit = new[] { new ReportingRunAuditEntry(operation.RunId, DateTimeOffset.UtcNow, "RunGenerated", "operator", "Historical raw parameter fixture") };
        var store = new PostgresReportingRunStore(database.Options);

        await store.SaveAsync(manifest, audit, CancellationToken.None);
        var restarted = new PostgresReportingRunStore(database.Options);
        var reloaded = restarted.GetManifest("tenant-a", operation.RunId);

        reloaded.Should().BeEquivalentTo(manifest);
        reloaded!.CertifiedSnapshot!.ParametersCanonicalJson.Should().Be(frozenRaw);
        reloaded.CertifiedSnapshot.ParametersHash.Should().Be(frozenHash);
        reloaded.CertifiedSnapshot.SnapshotHash.Should().Be(snapshotHash);
        ReportingCanonicalParameterSerializer.Serialize(reloaded.ResolvedParameters!).Should().NotBe(frozenRaw);
        var retainedContext = new CertifiedReportingRunContext(reloaded.OperationalScope!, reloaded.ImmutableAccessScope!,
            reloaded.CertifiedSnapshot, reloaded.AuthoritativeSource!, reloaded.CertifiedDatasetRows, reloaded.Readiness!);
        var certification = new ReportingRunCertificationService();
        certification.BuildGovernanceReadiness(operation.RunId, retainedContext, reloaded.Readiness!)
            .SnapshotHash.Should().Be(snapshotHash);
        var rewrittenHash = retainedContext with
        {
            Snapshot = retainedContext.Snapshot with
            { ParametersHash = ReportingCanonicalParameterSerializer.ComputeHash(reloaded.ResolvedParameters!) }
        };
        var changedReceipt = () => certification.BuildGovernanceReadiness(operation.RunId, rewrittenHash, reloaded.Readiness!);
        changedReceipt.Should().Throw<InvalidOperationException>().WithMessage("*exact readiness and source checkpoint*");
        var revision = restarted.GetRevision("tenant-a", operation.RunId);
        revision.Should().NotBeNull();
        var changed = new[]
        {
            frozenRaw.Replace(rawMap, rawMap.Replace("capitalCallId", "unknownCallId"), StringComparison.Ordinal),
            frozenRaw.Replace(rawMap, rawMap.Replace("legacy-call", "other-call"), StringComparison.Ordinal),
            frozenRaw[..^1] + ",\"extra\":true}"
        };
        foreach (var forgedJson in changed)
        {
            var forged = manifest with
            {
                CertifiedSnapshot = snapshot with
                { ParametersCanonicalJson = forgedJson, ParametersHash = Sha256Digest.ComputeUtf8(forgedJson) }
            };
            forged = forged with
            {
                CertifiedSnapshot = forged.CertifiedSnapshot! with
                { SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(forged) }
            };
            var save = () => restarted.SaveAsync(forged, audit, revision, CancellationToken.None);
            await save.Should().ThrowAsync<InvalidDataException>().WithMessage("*mismatched source bindings*");
        }
        restarted.GetManifest("tenant-a", operation.RunId).Should().BeEquivalentTo(manifest);
        restarted.GetAudit("tenant-a", operation.RunId).Select(entry => entry.Action).Should().Equal("RunGenerated");
        restarted.ListRuns("tenant-a", "company-a").Should().ContainSingle();
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

    private sealed class SummaryGovernanceRepository : IReportingGovernanceRepository
    {
        private readonly IReportingGovernanceTransaction _transaction = Substitute.For<IReportingGovernanceTransaction>();

        public SummaryGovernanceRepository(IReadOnlyDictionary<string, GovernedReportingRun> runs)
        {
            _transaction.GetRunsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(call => ValueTask.FromResult<IReadOnlyList<GovernedReportingRun>>(runs.Values
                    .Where(run => run.Scope.TenantId == call.ArgAt<string>(0)
                        && call.ArgAt<IReadOnlyCollection<string>>(1).Contains(run.RunId)).ToArray()));
        }

        public ValueTask<TResult> ExecuteTransactionAsync<TResult>(
            Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) => operation(_transaction, cancellationToken);
    }
}
