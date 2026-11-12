using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class PreparerAttestationApprovalBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-preparer-boundary-" + Guid.NewGuid().ToString("N"));
    private static readonly SinglePrincipalAttestationScopeDto Scope = new("tenant-one", "company-one");
    private static readonly SinglePrincipalAttestationAuthority Principal = new("preparer-one",
        OperationsActionOriginDto.HumanOperator, true, CanAdministerDeclaration: true,
        CanAttest: true, CanModifyTarget: true);
    private const string Operands = "{\"version\":1,\"document\":{\"amount\":100}}";
    private static readonly SinglePrincipalAttestationSubjectSnapshotDto Subject = new(
        "evidence-document", "vault-one/document-one", Sha256Digest.ComputeUtf8(Operands), Sha256Digest.ComputeUtf8(Operands),
        "artifact-one", Sha256Digest.ComputeUtf8("retained source"), "external-importer", "Synthetic", true,
        ["Verified retained source and exact scope"], "account", "account-one", Operands);

    private SinglePrincipalAttestationStore Store(bool enabled = true) => new(_root,
        (_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(Subject), enabled);

    [Theory]
    [InlineData("preparer-attestation:v1:00000000-0000-0000-0000-000000000001")]
    [InlineData(" PREPARER-ATTESTATION:future-version:opaque ")]
    [InlineData("/api/workstation/evidence/attestations/receipts/opaque")]
    [InlineData("https://meridian.local/api/workstation/evidence/attestations/receipts/opaque")]
    public void ReservedNamespaceCannotBeUsedAsOrdinaryApproval(string reference)
    {
        PreparerAttestationApprovalBoundary.IsReservedReference(reference).Should().BeTrue();
        var action = () => PreparerAttestationApprovalBoundary.RequireOrdinaryReference(reference, "approvalReference");
        action.Should().Throw<ArgumentException>().WithParameterName("approvalReference");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("approval:ordinary-control-reference")]
    public void UnrecognizedTextIsNotClassifiedAsIndependentReviewOrRewritten(string? reference)
    {
        PreparerAttestationApprovalBoundary.IsReservedReference(reference).Should().BeFalse();
        PreparerAttestationApprovalBoundary.RequireOrdinaryReference(reference, "approvalReference");
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    public async Task ExactRetainedReceiptIdentityIsDeniedWithoutRelabelingCallerChosenCommandIdentity(string format)
    {
        var receipt = await RetainAsync();
        var classifier = (IPreparerAttestationReferenceClassifier)Store(enabled: false);

        (await classifier.IsPreparerReceiptReferenceAsync(Scope.TenantId, Scope.CompanyId, receipt.ReceiptId.ToString(format)))
            .Should().BeTrue();
        (await classifier.IsPreparerReceiptReferenceAsync(Scope.TenantId, Scope.CompanyId, receipt.CommandId.ToString(format)))
            .Should().BeFalse("an idempotency identity is caller-chosen and cannot relabel an unrelated ordinary approval");
        (await classifier.IsPreparerReceiptReferenceAsync("different-tenant", Scope.CompanyId, receipt.ReceiptId.ToString(format)))
            .Should().BeTrue("the reserved namespace is a denial category without reading another tenant's receipt history");
    }

    [Fact]
    public void NewReceiptIdsUseAReservedDenialNamespaceWithoutChangingOrdinaryUuid4Ids()
    {
        var identities = Enumerable.Range(0, 100).Select(_ => PreparerAttestationApprovalBoundary.NewReceiptId()).ToArray();
        identities.Should().OnlyContain(identity => PreparerAttestationApprovalBoundary.IsReservedReceiptId(identity));
        identities.Distinct().Should().HaveCount(100);
        var ordinary = Guid.Parse("a77e57ed-1111-4111-8111-111111111111");
        PreparerAttestationApprovalBoundary.IsReservedReceiptId(ordinary).Should().BeFalse();
        PreparerAttestationApprovalBoundary.IsReservedReference(ordinary.ToString("D")).Should().BeFalse();
        var nonexistent = Guid.Parse("a77e57ed-1111-8111-8111-111111111111");
        foreach (var format in new[] { "D", "N", "B", "P", "X" })
            PreparerAttestationApprovalBoundary.IsReservedReference(nonexistent.ToString(format)).Should().BeTrue(
                "a reserved ID cannot be ordinary authority merely because its receipt was not found");
    }

    [Fact]
    public async Task ScopedLegacyUuid4LookupDoesNotInspectForeignTenantHistory()
    {
        var foreignIdentity = Guid.NewGuid().ToString("D");
        (await Store().IsPreparerReceiptReferenceAsync("different-tenant", Scope.CompanyId, foreignIdentity)).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task HistoricalUuid4ReceiptRemainsUnchangedAndIsClassifiedOnlyWithinItsScope()
    {
        var receipt = await RetainAsync();
        var legacyId = Guid.NewGuid();
        var path = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var payload = envelope["payload"]!.AsObject();
        // Construct a retained v4 fixture from the pre-namespace contract; production code
        // never rewrites historical IDs or their classifications.
        payload["receipts"]![0]!["receiptId"] = legacyId.ToString("D");
        foreach (var audit in payload["auditTrail"]!.AsArray())
            if (audit!["receiptId"]?.GetValue<string>() == receipt.ReceiptId.ToString("D"))
                audit["receiptId"] = legacyId.ToString("D");
        foreach (var command in payload["commands"]!.AsArray())
            if (command!["result"]?["receiptId"]?.GetValue<string>() == receipt.ReceiptId.ToString("D"))
                command["result"]!["receiptId"] = legacyId.ToString("D");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // The durable checksum is over typed snapshot serialization. JsonNode serialization
        // can preserve a different spelling of nested JsonElement strings than that typed
        // round trip, so canonicalize the legacy fixture by the persisted contract first.
        var legacySnapshot = payload.Deserialize<LegacySnapshot>(options)!;
        var canonicalPayload = JsonSerializer.Serialize(legacySnapshot, options);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            Payload = legacySnapshot, Hash = Sha256Digest.ComputeUtf8(canonicalPayload)
        }, options));
        var retainedBytes = await File.ReadAllTextAsync(path);

        var classifier = Store(enabled: false);
        (await classifier.IsPreparerReceiptReferenceAsync(Scope.TenantId, Scope.CompanyId, legacyId.ToString("D")))
            .Should().BeTrue();
        (await classifier.IsPreparerReceiptReferenceAsync("foreign-tenant", Scope.CompanyId, legacyId.ToString("D")))
            .Should().BeFalse();
        (await File.ReadAllTextAsync(path)).Should().Be(retainedBytes);
    }

    [Fact]
    public async Task WithdrawalAndRevocationCannotTurnAnAssertionIntoOrdinaryApproval()
    {
        var receipt = await RetainAsync();
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal,
            new(Guid.NewGuid(), 1, false, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "Withdraw declaration"));
        await store.RevokeAsync(Scope, Principal,
            new(Guid.NewGuid(), receipt.ReceiptId, 1, "Correct erroneous assertion"));

        (await Store(enabled: false).IsPreparerReceiptReferenceAsync(
            Scope.TenantId, Scope.CompanyId, receipt.ReceiptId.ToString("D"))).Should().BeTrue();
    }

    [Fact]
    public async Task MissingStateHasNoReceipts_ButCorruptStateCannotSupplyAnOrdinaryClassification()
    {
        var unknownId = Guid.NewGuid().ToString("D");
        (await Store().IsPreparerReceiptReferenceAsync(Scope.TenantId, Scope.CompanyId, unknownId)).Should().BeFalse();
        await RetainAsync();
        var snapshot = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        await File.WriteAllTextAsync(snapshot, "corrupt state");

        var error = await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() =>
            Store().IsPreparerReceiptReferenceAsync(Scope.TenantId, Scope.CompanyId, unknownId));
        error.Code.Should().Be("state_unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectReconciliationCannotUseReceiptAsReferenceOrApprovalActor(bool actorField)
    {
        var receipt = await RetainAsync();
        var (repo, queueScope, item) = await QueueAsync(Store());
        var command = Waive(item) with
        {
            ApprovalReference = actorField ? "approval:ordinary-reference" : receipt.ReceiptId.ToString("D"),
            ApprovalActor = actorField ? receipt.ReceiptId.ToString("D") : "ordinary-reviewer"
        };

        var result = await repo.ApplyCaseworkCommandAsync(queueScope, command);

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingApproval);
        result.Error.Should().Contain("preparer attestation");
        (await repo.GetByIdAsync(queueScope, item.BreakId))!.Version.Should().Be(item.Version);
    }

    [Fact]
    public async Task DirectReconciliationRejectsReservedReferenceEvenWithoutClassifierRegistration()
    {
        var (repo, queueScope, item) = await QueueAsync();
        var result = await repo.ApplyCaseworkCommandAsync(queueScope, Waive(item) with
        {
            ApprovalReference = PreparerAttestationApprovalBoundary.ReceiptReference(Guid.NewGuid())
        });

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        result.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingApproval);
        (await repo.GetByIdAsync(queueScope, item.BreakId))!.Version.Should().Be(item.Version);
    }

    [Fact]
    public async Task OrdinaryOpaqueReferenceRetainsExistingBehaviorWithoutInferringIndependence()
    {
        var (repo, queueScope, item) = await QueueAsync(Store());
        var result = await repo.ApplyCaseworkCommandAsync(queueScope, Waive(item));

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        result.Item!.DispositionApprovalReference.Should().Be("approval:ordinary-reference");
    }

    [Fact]
    public async Task CallerChosenCommandIdentityCannotBlacklistAnOrdinaryApprovalReference()
    {
        var ordinaryApprovalId = Guid.NewGuid();
        var receipt = await RetainAsync(ordinaryApprovalId);
        receipt.ReceiptId.Should().NotBe(ordinaryApprovalId);
        var (repo, queueScope, item) = await QueueAsync(Store());

        var result = await repo.ApplyCaseworkCommandAsync(queueScope, Waive(item) with
        {
            ApprovalReference = ordinaryApprovalId.ToString("D")
        });

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        result.Item!.DispositionApprovalReference.Should().Be(ordinaryApprovalId.ToString("D"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkApplyAndPreviewCannotUseARealPreparerReceipt(bool dryRun)
    {
        var receipt = await RetainAsync();
        var (repo, queueScope, item) = await QueueAsync(Store());
        var second = item with { BreakId = "break-two" };
        await repo.CreateIfMissingAsync(queueScope, second);
        second = (await repo.GetByIdAsync(queueScope, second.BreakId))!;

        var result = await repo.ApplyBulkCaseworkAsync(queueScope, new(
            [item.BreakId, second.BreakId], ReconciliationCaseworkAction.Waive, "resolving-operator",
            Guid.NewGuid().ToString("D"), "boundary-test", "synthetic-test", Guid.NewGuid().ToString("D"),
            dryRun, false, Reason: "Documented ordinary disposition", EvidenceLinks: ["evidence:retained-support"],
            ApprovalActor: "ordinary-reviewer", ApprovalReference: receipt.ReceiptId.ToString("D")));

        result.SucceededCount.Should().Be(0);
        result.Results.Should().OnlyContain(entry => !entry.Succeeded && !entry.WouldSucceed
            && entry.Error!.Contains("preparer attestation", StringComparison.OrdinalIgnoreCase));
        (await repo.GetByIdAsync(queueScope, item.BreakId))!.Version.Should().Be(item.Version);
        (await repo.GetByIdAsync(queueScope, second.BreakId))!.Version.Should().Be(second.Version);
    }

    [Fact]
    public async Task UnavailableClassificationDeniesNewDispositionBeforeMutation()
    {
        await RetainAsync();
        var snapshot = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        await File.WriteAllTextAsync(snapshot, "corrupt state");
        var (repo, queueScope, item) = await QueueAsync(Store());
        var result = await repo.ApplyCaseworkCommandAsync(queueScope, Waive(item) with
        {
            ApprovalReference = Guid.NewGuid().ToString("D")
        });

        result.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        result.Error.Should().Contain("classification is unavailable");
        (await repo.GetByIdAsync(queueScope, item.BreakId))!.Version.Should().Be(item.Version);
    }

    private async Task<PreparerAttestationReceiptDto> RetainAsync(Guid? commandId = null)
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal,
            new(Guid.NewGuid(), 0, true, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "One principal controls this scope"));
        return await store.AttestAsync(Scope, Principal,
            new(commandId ?? Guid.NewGuid(), 1, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment,
                Subject.SubjectKind, Subject.SubjectId, Subject.Revision, Subject.Hash,
                "Inspected exact retained source", SinglePrincipalAttestationPolicy.Confirmation));
    }

    private async Task<(FileReconciliationBreakQueueRepository Repo, ReconciliationBreakQueueScope Scope, ReconciliationBreakQueueItem Item)> QueueAsync(
        IPreparerAttestationReferenceClassifier? classifier = null)
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(_root, "queue"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance, preparerAttestationReferences: classifier);
        var queueScope = new ReconciliationBreakQueueScope(Scope.TenantId, Scope.CompanyId);
        var now = DateTimeOffset.UtcNow;
        var item = new ReconciliationBreakQueueItem("break-one", "run-one", "Synthetic boundary test",
            ReconciliationBreakCategory.CashMismatch, ReconciliationBreakQueueStatus.Open, 10m,
            "Retained source differs", null, now, now)
        {
            TenantId = Scope.TenantId, CompanyId = Scope.CompanyId
        };
        await repo.CreateIfMissingAsync(queueScope, item);
        return (repo, queueScope, (await repo.GetByIdAsync(queueScope, item.BreakId))!);
    }

    private static ReconciliationCaseworkCommand Waive(ReconciliationBreakQueueItem item) => new(
        item.BreakId, ReconciliationCaseworkAction.Waive, "resolving-operator", Guid.NewGuid().ToString("D"),
        CorrelationId: "boundary-test", Source: "workstation",
        ExpectedVersion: item.Version, Reason: "Documented ordinary disposition",
        EvidenceLinks: ["evidence:retained-support"], ApprovalActor: "ordinary-reviewer",
        ApprovalReference: "approval:ordinary-reference");

    private sealed record LegacyCompletedCommand(Guid CommandId, string Fingerprint, JsonElement Result);
    private sealed record LegacySnapshot(int SchemaVersion, SinglePrincipalAttestationScopeDto Scope,
        SinglePrincipalDeclarationDto[] Declarations, PreparerAttestationReceiptDto[] Receipts,
        PreparerAttestationRevocationDto[] Revocations, SinglePrincipalAttestationAuditEventDto[] AuditTrail,
        LegacyCompletedCommand[] Commands);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
