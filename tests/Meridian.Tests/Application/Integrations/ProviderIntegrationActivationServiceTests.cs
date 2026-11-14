using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationActivationServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationActivationServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_activation_command_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ActivateAsync_PersistsActiveManifestAndConnectionWhenReadinessPasses()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity", "asOf"],
            mappings:
            [
                Mapping("providerAccountId"),
                Mapping("quantity"),
                Mapping("asOf")
            ]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(store, manifest, connection);
        var service = new ProviderIntegrationActivationService(store);

        var result = await service.ActivateAsync(CreateRequest(manifest, connection));

        result.Activated.Should().BeTrue();
        result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.Active);
        result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.Active);
        result.Readiness.IsReady.Should().BeTrue();
        result.ApprovalEvidenceId.Should().Be("approval-evidence-activation-1");
        var savedManifest = await store.GetManifestAsync(manifest.ManifestId);
        savedManifest.Should().NotBeNull();
        savedManifest!.ManifestVersion.Should().Be(manifest.ManifestVersion + 1);
        savedManifest!.State.Should().Be(ProviderIntegrationActivationStateDto.Active);
        savedManifest.ApprovedBy.Should().Be("approver@example.com");
        savedManifest.ApprovedAt.Should().Be(DateTimeOffset.Parse("2026-06-16T14:00:00Z"));
        savedManifest.ChangeReason.Should().Be("Approved after dry-run evidence review.");
        result.ManifestReference.Should().Be(ProviderIntegrationManifestIdentity.Create(savedManifest));
        (await store.GetManifestVersionAsync(manifest.ManifestId, manifest.ManifestVersion)).Should()
            .BeEquivalentTo(manifest);
        (await store.GetManifestVersionAsync(savedManifest.ManifestId, savedManifest.ManifestVersion)).Should()
            .BeEquivalentTo(savedManifest);
        var savedConnection = await store.GetConnectionAsync(connection.ConnectionId);
        savedConnection.Should().NotBeNull();
        savedConnection!.State.Should().Be(ProviderIntegrationActivationStateDto.Active);
        savedConnection.ApprovalEvidenceId.Should().Be("approval-evidence-activation-1");
    }

    [Fact]
    public async Task ActivateAsync_RejectsApprovalForStaleManifestReference()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId"), Mapping("quantity")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var updatedManifest = manifest with { ManifestVersion = 2, ChangeReason = "Mapping review completed." };
        await store.SaveManifestVersionAsync(updatedManifest);
        (await store.CompareExchangeCurrentManifestAsync(
            manifest.ManifestId,
            ProviderIntegrationManifestIdentity.Create(manifest),
            ProviderIntegrationManifestIdentity.Create(updatedManifest))).Should().BeTrue();
        var service = new ProviderIntegrationActivationService(store);
        var request = CreateRequest(manifest, connection) with
        {
            ExpectedManifestReference = ProviderIntegrationManifestIdentity.Create(manifest)
        };

        var act = () => service.ActivateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest changed*");
        (await store.GetManifestAsync(manifest.ManifestId)).Should().BeEquivalentTo(updatedManifest);
        (await store.GetManifestVersionAsync(manifest.ManifestId, 3)).Should().BeNull();
        (await store.GetConnectionAsync(connection.ConnectionId)).Should().BeEquivalentTo(connection);
    }

    [Fact]
    public async Task ActivateAsync_RequiresReviewedManifestReference()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId"), Mapping("quantity")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);
        var request = CreateRequest(manifest, connection) with { ExpectedManifestReference = null };

        var act = () => service.ActivateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest reference that was reviewed*");
        (await store.GetManifestAsync(manifest.ManifestId)).Should().BeEquivalentTo(manifest);
        (await store.GetManifestVersionAsync(manifest.ManifestId, 2)).Should().BeNull();
        (await store.GetConnectionAsync(connection.ConnectionId)).Should().BeEquivalentTo(connection);
    }

    [Fact]
    public async Task ActivateAsync_DoesNotPersistWhenReadinessHasCriticalIssues()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);

        var result = await service.ActivateAsync(CreateRequest(manifest, connection));

        result.Activated.Should().BeFalse();
        result.ManifestState.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        result.ConnectionState.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        result.Readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.required-mapping-missing" &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        (await store.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        (await store.GetConnectionAsync(connection.ConnectionId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
    }

    [Fact]
    public async Task ActivateAsync_ObservesCancellationBeforePersistingActivation()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(
            requiredFields: ["providerAccountId", "quantity"],
            mappings: [Mapping("providerAccountId"), Mapping("quantity")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var service = new ProviderIntegrationActivationService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ActivateAsync(CreateRequest(manifest, connection), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetManifestAsync(manifest.ManifestId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
        (await store.GetConnectionAsync(connection.ConnectionId))!.State.Should().Be(ProviderIntegrationActivationStateDto.DryRunPassed);
    }

    [Theory]
    [InlineData(ProviderIntegrationActivationStateDto.Draft, "missing")]
    [InlineData(ProviderIntegrationActivationStateDto.DryRunPassed, "missing")]
    [InlineData(ProviderIntegrationActivationStateDto.DryRunPassed, "partial")]
    [InlineData(ProviderIntegrationActivationStateDto.DryRunPassed, "interrupted")]
    [InlineData(ProviderIntegrationActivationStateDto.DryRunPassed, "missing-raw")]
    [InlineData(ProviderIntegrationActivationStateDto.DryRunPassed, "missing-row")]
    public async Task ActivateAsync_RefusesClaimedStatesWithoutCompleteRetainedEvidence(ProviderIntegrationActivationStateDto state, string evidence)
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(["providerAccountId", "quantity"], [Mapping("providerAccountId"), Mapping("quantity")]) with { State = state };
        var connection = CreateConnection(manifest) with { State = state };
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        if (evidence != "missing")
        {
            var run = await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(store, manifest, connection, retainRaw: evidence != "missing-raw");
            run = evidence switch
            {
                "partial" => run with
                {
                    Status = ProviderIntegrationProcessingStatusDto.Quarantined,
                    Issues = [new("http.response.incomplete", ProviderIntegrationIssueSeverityDto.Critical, "Incomplete response.", null, null)]
                },
                "interrupted" => run with { Status = ProviderIntegrationProcessingStatusDto.Received, CompletedAt = null },
                "missing-row" => run with { RecordsReceived = 1, RecordsAccepted = 1 },
                _ => run
            };
            await store.SaveSyncRunAsync(run);
        }

        var readiness = await new ProviderIntegrationActivationReadinessService(store).EvaluateAsync(manifestId: manifest.ManifestId, connectionId: connection.ConnectionId);
        var result = await new ProviderIntegrationActivationService(store).ActivateAsync(CreateRequest(manifest, connection));

        readiness.IsReady.Should().BeFalse();
        result.Activated.Should().BeFalse();
        result.Readiness.Issues.Should().Contain(issue => issue.Code == "provider-manifest.dry-run-evidence-required");
        (await store.GetManifestAsync(manifest.ManifestId)).Should().BeEquivalentTo(manifest);
        (await store.GetConnectionAsync(connection.ConnectionId)).Should().BeEquivalentTo(connection);
        (await store.GetManifestVersionAsync(manifest.ManifestId, manifest.ManifestVersion + 1)).Should().BeNull();
    }

    [Fact]
    public async Task ActivateAsync_RejectsLatestPartialRunInsteadOfReusingEarlierSuccess()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(["providerAccountId"], [Mapping("providerAccountId")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(store, manifest, connection, "old-complete");
        var newer = await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(store, manifest, connection,
            "new-partial", DateTimeOffset.Parse("2026-06-16T12:35:00Z"));
        await store.SaveSyncRunAsync(newer with { Status = ProviderIntegrationProcessingStatusDto.Quarantined });

        var result = await new ProviderIntegrationActivationService(store).ActivateAsync(CreateRequest(manifest, connection));

        result.Activated.Should().BeFalse();
        result.Readiness.Issues.Should().Contain(issue => issue.Code == "provider-manifest.dry-run-evidence-required");
    }

    [Fact]
    public async Task ActivateAsync_DoesNotReuseEvidenceAfterMappingChange()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = CreateManifest(["providerAccountId"], [Mapping("providerAccountId")]);
        var connection = CreateConnection(manifest);
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(store, manifest, connection);
        var changed = manifest with { ManifestVersion = 2, FieldMappings = [Mapping("providerAccountId") with { SourcePath = "$.different_account" }] };
        await store.SaveManifestVersionAsync(changed);
        (await store.CompareExchangeCurrentManifestAsync(manifest.ManifestId,
            ProviderIntegrationManifestIdentity.Create(manifest), ProviderIntegrationManifestIdentity.Create(changed))).Should().BeTrue();

        var result = await new ProviderIntegrationActivationService(store).ActivateAsync(CreateRequest(changed, connection));

        result.Activated.Should().BeFalse();
        (await store.GetManifestVersionAsync(manifest.ManifestId, 3)).Should().BeNull();
    }

    [Fact]
    public async Task ActivateAsync_UsesActualDraftDryRunAndPreservesItsHistoricalReferenceAcrossActivation()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-custodian-positions-v1")!;
        var connection = CreateConnection(manifest) with { State = ProviderIntegrationActivationStateDto.Draft };
        await store.SaveManifestAsync(manifest);
        await store.SaveConnectionAsync(connection);
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        var dryRun = await new ProviderIntegrationRestDryRunService(store, new CompleteTransport()).RunRestDryRunAsync(new(
            "actual-draft-source", manifest.ManifestId, connection.ConnectionId, ProviderCapabilityKindDto.Positions, "positions",
            new Dictionary<string, string> { ["accountId"] = "A-100" }, new Dictionary<string, string>(), "operator",
            DateTimeOffset.Parse("2026-06-16T12:30:00Z"), MaxPages: 1));
        dryRun.Status.Should().Be(ProviderIntegrationProcessingStatusDto.Validated);
        dryRun.RecordsAccepted.Should().Be(1);

        var result = await new ProviderIntegrationActivationService(store).ActivateAsync(CreateRequest(manifest, connection));
        var currentReadiness = await new ProviderIntegrationActivationReadinessService(store).EvaluateAsync(manifestId: manifest.ManifestId, connectionId: connection.ConnectionId);

        result.Activated.Should().BeTrue();
        result.ManifestReference!.ManifestVersion.Should().Be(manifest.ManifestVersion + 1);
        currentReadiness.IsReady.Should().BeTrue("lifecycle-only activation does not invalidate exact historical source evidence");
        (await store.GetSyncRunAsync(dryRun.SyncRunId))!.ManifestReference.Should().Be(reference);
        (await store.GetManifestVersionAsync(manifest.ManifestId, manifest.ManifestVersion)).Should().BeEquivalentTo(manifest);
    }

    [Fact]
    public async Task ActivateAsync_DoesNotBorrowEvidenceFromAnotherTenantWithTheSameConnectionId()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var tenantA = store.ForTenant("tenant-a");
        var tenantB = store.ForTenant("tenant-b");
        var manifest = CreateManifest(["providerAccountId"], [Mapping("providerAccountId")]);
        var connection = CreateConnection(manifest);
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await tenant.SaveManifestAsync(manifest);
            await tenant.SaveConnectionAsync(connection);
        }
        await ProviderIntegrationCompletionFixture.RetainEmptySourceAsync(tenantA, manifest, connection);

        var result = await new ProviderIntegrationActivationService(store).ActivateAsync("tenant-b", CreateRequest(manifest, connection));

        result.Activated.Should().BeFalse();
        (await tenantB.GetManifestAsync(manifest.ManifestId)).Should().BeEquivalentTo(manifest);
        (await tenantB.ListSyncRunsAsync(connection.ConnectionId)).Should().BeEmpty();
        (await tenantA.ListSyncRunsAsync(connection.ConnectionId)).Should().ContainSingle();
    }

    private sealed class CompleteTransport : IProviderIntegrationHttpTransport
    {
        public Task<ProviderIntegrationHttpResponse> SendAsync(ProviderIntegrationHttpRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProviderIntegrationHttpResponse(200, new Dictionary<string, string>(),
                """{"positions":[{"account_id":"A-100","cusip":"9128285M8","quantity":"100","currency":"USD","as_of_date":"2026-06-16","position_id":"POS-1"}]}"""));
    }

    private static ProviderIntegrationActivationRequestDto CreateRequest(
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection)
        => new(
            manifest.ManifestId,
            connection.ConnectionId,
            "approver@example.com",
            DateTimeOffset.Parse("2026-06-16T14:00:00Z"),
            "approval-evidence-activation-1",
            "Approved after dry-run evidence review.")
        {
            ExpectedManifestReference = ProviderIntegrationManifestIdentity.Create(manifest)
        };

    private static ProviderIntegrationManifestDto CreateManifest(
        IReadOnlyList<string> requiredFields,
        IReadOnlyList<FieldMappingDto> mappings)
        => new(
            "manifest-activation-command-v1",
            1,
            "provider-alpha",
            "Provider Alpha",
            IntegrationTypeDto.Rest,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string>()),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    requiredFields)
            ],
            [],
            mappings,
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes: []),
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Activation command test manifest");

    private static FieldMappingDto Mapping(string targetField)
        => new(
            ProviderCapabilityKindDto.Positions,
            $"$.{targetField.Replace('.', '_')}",
            targetField,
            null,
            Required: true,
            ProviderMappingConfidenceDto.Approved,
            DefaultValue: null,
            ConstantValue: null);

    private static ProviderConnectionDto CreateConnection(ProviderIntegrationManifestDto manifest)
        => new(
            "connection-activation-command",
            manifest.ProviderId,
            manifest.ManifestId,
            "Provider Alpha Production",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.DryRunPassed,
            "vault://provider-credentials/provider-alpha/production",
            [ProviderCapabilityKindDto.Positions],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            ApprovalEvidenceId: null);
}
