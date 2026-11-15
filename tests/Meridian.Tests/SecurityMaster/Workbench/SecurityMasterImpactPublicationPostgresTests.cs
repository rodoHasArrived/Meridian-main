using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterImpactPublicationPostgresTests(SecurityMasterDatabaseFixture fixture) : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task StrictPublication_JoinsCanonicalWritesAndRevisionTransition_AndRollsBackStaleFinalization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var options = fixture.Options;
        var events = new PostgresSecurityMasterEventStore(options);
        var snapshots = new PostgresSecurityMasterSnapshotStore(options);
        var securities = new PostgresSecurityMasterStore(options);
        var revisions = new PostgresSecurityMasterRevisionStore(options);
        using var canonical = new SecurityMasterService(events, snapshots, securities, new(events, snapshots), options,
            NullLogger<SecurityMasterService>.Instance, canonicalWriteScope: new PostgresSecurityMasterCanonicalWriteScope(options), revisionStore: revisions);
        var effective = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var security = await canonical.CreateAsync(new(Guid.NewGuid(), "FxSpot",
            JsonSerializer.SerializeToElement(new { displayName = "Impact atomicity", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, baseCurrency = "EUR", quoteCurrency = "USD" }),
            [new(SecurityIdentifierKind.InternalCode, "IMPACT-" + Guid.NewGuid().ToString("N"), true, effective)],
            effective, "test-source", "maker", null, "Synthetic atomicity evidence"), ct);
        var effects = new CommitObservationHandler();
        var competing = new BeforeMergeHandler();
        var impact = Substitute.For<ISecurityMasterImpactPreviewService>();
        var scope = Substitute.For<ISecurityMasterImpactScopeAccessor>();
        var tenant = new SecurityMasterImpactScope("synthetic-impact");
        scope.GetRequired().Returns(tenant);
        impact.AcquireFinalizationLeaseAsync(Arg.Any<SecurityMasterRevisionRecord>(), tenant, Arg.Any<CancellationToken>())
            .Returns(_ => new NoopLease());
        var restatement = Substitute.For<IPeriodAwareRestatementResolver>();
        restatement.ResolveAsync(Arg.Any<SecurityMasterRevisionPublishedEvent>(), Arg.Any<CancellationToken>()).Returns(new SecurityMasterRestatementDecision(false, []));
        var readModel = Substitute.For<ISecurityMasterWorkbenchQueryService>();
        readModel.GetInstrumentPassportAsync(security.SecurityId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InstrumentPassportDto(security.SecurityId, null!,
                new SecurityMasterEconomicDefinitionDrillInDto(security.SecurityId, "FxSpot", "USD", security.Version,
                    effective, null, null, null, null, null, null, null, null, null, null),
                null!, [], [], [], null!, null!, null!, effective));
        var workflow = Substitute.For<IOperationsContinuityWorkflowService>();
        workflow.ApproveWorkflowAsync(Arg.Any<Guid>(), Arg.Any<OperationsApprovalDecisionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new OperationsTransitionResultDto(true, null, null, null, [], [], 5));
        var governance = Substitute.For<ISecurityMasterGovernanceRequirements>();
        governance.Current.Returns(new SecurityMasterGovernanceSnapshot(SecurityMasterGovernanceProfile.Institutional,
            SecurityMasterGovernanceProfileSource.Configuration, SecurityMasterGovernanceRequirementBasis.Profile,
            SecurityMasterGovernanceRequirementBasis.Profile, SecurityMasterGovernanceRequirementBasis.Profile, []));
        var merge = new ApprovedFieldEditCanonicalMergeHandler(revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, securities, canonical);
        var workbench = new SecurityMasterWorkbenchCommandService(events, new PostgresOperatorOverridesStore(options),
            Substitute.For<ISecurityMasterConflictAuthorityPolicy>(), Substitute.For<ISecurityMasterConflictService>(),
            readModel, workflow, revisions,
            restatement, Substitute.For<IAffectedLedgerBookResolver>(), [competing, merge, effects], NullLogger<SecurityMasterWorkbenchCommandService>.Instance,
            projectionStore: securities, governanceRequirements: governance, impactPreview: impact, impactScope: scope);
        var edit = await workbench.UpdateSecurityFieldAsync(new(security.SecurityId, security.Version,
            "assetSpecificTerms.baseCurrency", "GBP", effective, "maker", "Synthetic reviewed repair"), ct);
        var workflowId = Guid.NewGuid();
        var revision = await revisions.TransitionAsync(edit.RevisionId, SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted, "submitter", workflowIdForSubmit: workflowId, ct: ct);
        var receiptId = new string('a', 64);
        var projection = (await securities.GetProjectionAsync(security.SecurityId, ct))!;
        var before = SecurityMasterMapping.ToDetail(projection);
        var after = before with { AssetSpecificTerms = new SecurityMasterProposedTermsBuilder().Build(projection, revision) };
        var receipt = new SecurityMasterImpactPreviewReceipt("test", tenant, security.SecurityId, edit.RevisionId, security.Version,
            "", "", before, after, effective, effective, new(SecurityImpactCompleteness.Partial, [], "Conservative synthetic scope with no books", []), [],
            new(SecurityImpactCompleteness.Unavailable, [], "", []), null, null, [], "", receiptId);
        var checks = 0;
        var refuseFinal = true;
        impact.ValidateAsync(Arg.Any<SecurityMasterRevisionRecord>(), tenant, receiptId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                checks++;
                if (call.ArgAt<bool>(3) && refuseFinal)
                    throw new SecurityMasterImpactPreviewException("ImpactDependenciesChanged", "Synthetic stale finalization.");
                return receipt;
            });
        impact.GetAsync(tenant, receiptId, Arg.Any<CancellationToken>()).Returns(receipt);
        var approval = await workbench.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(security.SecurityId,
            edit.RevisionId, workflowId, 1, "reviewer", "reviewer", "Synthetic receipt-backed approval", "")
        { ImpactReceiptId = receiptId }, ct);
        approval.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        (await revisions.GetAsync(edit.RevisionId, ct))!.ReviewedImpactReceiptId.Should().Be(receiptId);
        var publish = new PublishSecurityMasterRevisionRequest(security.SecurityId, edit.RevisionId, "publisher", "reviewer") { ImpactReceiptId = receiptId };
        await FluentActions.Invoking(() => workbench.PublishRevisionAsync(publish, ct)).Should().ThrowAsync<SecurityMasterImpactPreviewException>();
        checks.Should().BeGreaterThan(1);
        (await events.LoadAsync(security.SecurityId, ct)).Should().ContainSingle("the failed final check rolls canonical event append back");
        (await securities.GetProjectionAsync(security.SecurityId, ct))!.AssetSpecificTerms.GetProperty("baseCurrency").GetString().Should().Be("EUR");
        (await revisions.GetAsync(edit.RevisionId, ct))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        effects.Committed.Should().BeFalse("derived state must not observe an uncommitted publication");
        refuseFinal = false;
        var directName = "impact-competing-" + Guid.NewGuid().ToString("N");
        var directOptions = new SecurityMasterOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            { ApplicationName = directName }.ConnectionString,
            Schema = options.Schema,
            PreloadProjectionCache = false
        };
        var directEvents = new PostgresSecurityMasterEventStore(directOptions);
        var directSnapshots = new PostgresSecurityMasterSnapshotStore(directOptions);
        var directStore = new PostgresSecurityMasterStore(directOptions);
        using var direct = new SecurityMasterService(directEvents, directSnapshots, directStore, new(directEvents, directSnapshots),
            directOptions, NullLogger<SecurityMasterService>.Instance, canonicalWriteScope: new PostgresSecurityMasterCanonicalWriteScope(directOptions));
        Task<SecurityDetailDto>? directWrite = null;
        competing.Run = async () =>
        {
            // The competing ordinary amendment acquires the process gate, then waits for the
            // publisher's database stream lock. The publisher must never wait for that process gate.
            using (ExecutionContext.SuppressFlow())
                directWrite = Task.Run(() => direct.AmendTermsAsync(new(security.SecurityId, security.Version, null,
                    JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, baseCurrency = "CHF", quoteCurrency = "USD" }), [], [], effective,
                    "competing-source", "competing-writer", null, "Synthetic competing amendment"), ct), ct);
            await using var monitor = new NpgsqlConnection(options.ConnectionString);
            await monitor.OpenAsync(ct);
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                await using var command = monitor.CreateCommand();
                command.CommandText = "select count(*) from pg_stat_activity where application_name = @name and wait_event_type = 'Lock' and wait_event = 'advisory'";
                command.Parameters.AddWithValue("name", directName);
                if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0)
                    return;
                if (directWrite.IsCompleted)
                    await directWrite;
                await Task.Delay(10, ct);
            }
            throw new TimeoutException("The competing canonical writer did not reach its database lock wait.");
        };
        await workbench.PublishRevisionAsync(publish, ct);
        await FluentActions.Invoking(() => directWrite!).Should().ThrowAsync<SecurityMasterStreamVersionConflictException>();
        (await events.LoadAsync(security.SecurityId, ct)).Should().HaveCount(2);
        (await securities.GetProjectionAsync(security.SecurityId, ct))!.AssetSpecificTerms.GetProperty("baseCurrency").GetString().Should().Be("GBP");
        (await revisions.GetAsync(edit.RevisionId, ct))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        effects.Committed.Should().BeTrue();
    }

    private sealed class BeforeMergeHandler : ISecurityMasterRevisionPublishedHandler
    {
        public int Order => 0;
        public Func<Task> Run { get; set; } = () => Task.CompletedTask;
        public Task HandleAsync(SecurityMasterRevisionPublishedEvent publishedEvent, CancellationToken ct = default) => Run();
    }

    private sealed class NoopLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class CommitObservationHandler : ISecurityMasterRevisionPublishedHandler
    {
        public int Order => 50;
        public bool Committed { get; private set; }
        public Task HandleAsync(SecurityMasterRevisionPublishedEvent publishedEvent, CancellationToken ct = default)
            => PostgresSecurityMasterMutation.AfterCommitAsync(publishedEvent.SecurityId, () => { Committed = true; return Task.CompletedTask; });
    }
}
