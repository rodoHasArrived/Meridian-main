using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Ui.Shared.Evidence;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class SinglePrincipalAttestationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-attestation-{Guid.NewGuid():N}");
    private static readonly SinglePrincipalAttestationScopeDto Scope = new("tenant-a", "company-a");
    private static readonly SinglePrincipalAttestationAuthority Principal = new(
        "principal-a", OperationsActionOriginDto.HumanOperator, CanRead: true,
        CanAdministerDeclaration: true, CanAttest: true, CanModifyTarget: true);
    private const string SubjectSnapshotJson = "{\"version\":1,\"document\":\"document-a\",\"evaluatedAmount\":100}";
    private static readonly string SubjectSnapshotHash = Sha256Digest.ComputeUtf8(SubjectSnapshotJson);
    private static readonly SinglePrincipalAttestationSubjectSnapshotDto Subject = new(
        "evidence-document", "vault-a/document-a", SubjectSnapshotHash, SubjectSnapshotHash, "artifact-a", "source-hash",
        "external-importer", "Unspecified", true, ["Retained bytes verified", "Exact scope", "No unresolved checks"],
        "journal-entry", "journal-a", SubjectSnapshotJson);

    private SinglePrincipalAttestationStore Store(bool enabled = true,
        Func<SinglePrincipalAttestationScopeDto, SinglePrincipalAttestationAuthority,
            RecordPreparerAttestationRequestDto, CancellationToken, Task<SinglePrincipalAttestationSubjectSnapshotDto?>>? resolver = null,
        Func<string, string, CancellationToken, Task>? writer = null) => new(_root,
            resolver ?? ((_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(Subject)), enabled, writer);

    private static ChangeSinglePrincipalDeclarationRequestDto Declaration(long revision = 0, bool enabled = true) => new(
        Guid.NewGuid(), revision, enabled, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "One human controls this local scope");

    private static RecordPreparerAttestationRequestDto Acknowledgment(long declarationRevision = 1) => new(
        Guid.NewGuid(), declarationRevision, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment,
        Subject.SubjectKind, Subject.SubjectId, Subject.Revision, Subject.Hash,
        "Inspected original document and listed checks", SinglePrincipalAttestationPolicy.Confirmation);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task NewStore_IsDefaultOff_AndDeclarationAloneCannotEnableUnconfiguredPolicy()
    {
        var store = new SinglePrincipalAttestationStore(_root,
            (_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(Subject));

        var state = await store.GetAsync(Scope, Principal);
        state.PolicyEnabled.Should().BeFalse();
        state.Declaration.Should().BeNull();
        state.AllowedActions.Should().BeEmpty();
        await AssertDenied("policy_disabled", () => store.ChangeDeclarationAsync(Scope, Principal, Declaration()));
        (await store.GetAsync(Scope, Principal)).AuditTrail.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclarationRequiresSeparateGrant_AndCannotBindAnotherPrincipal()
    {
        var store = Store();
        await AssertDenied("declaration_permission", () => store.ChangeDeclarationAsync(Scope,
            Principal with { CanAdministerDeclaration = false }, Declaration()));
        await AssertDenied("principal_mismatch", () => store.ChangeDeclarationAsync(Scope, Principal,
            Declaration() with { PrincipalId = "different-human" }));
        await AssertDenied("unsupported_policy", () => store.ChangeDeclarationAsync(Scope, Principal,
            Declaration() with { PolicyVersion = "unknown" }));
        (await store.GetAsync(Scope, Principal)).AuditTrail.Should().BeEmpty();
    }

    [Fact]
    public async Task AcknowledgmentCreatesOwnAssertion_PreservesExternalProvenance_AndIsNotAnApproval()
    {
        var store = Store();
        var declaration = await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        var command = Acknowledgment();
        var receipt = await store.AttestAsync(Scope, Principal, command);

        receipt.DecisionKind.Should().Be("PreparerAttestation");
        receipt.Disclosure.Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
        receipt.AssertionId.Should().Be(command.CommandId);
        receipt.PreparedBy.Should().Be(Principal.Actor);
        receipt.AttestedBy.Should().Be(Principal.Actor);
        receipt.SourceActor.Should().Be("external-importer");
        receipt.SourceProvenance.Should().Be("Unspecified", "unknown provenance must never become live by default");
        receipt.DeclarationRevision.Should().Be(declaration.Revision);
        receipt.PolicyHash.Should().Be(SinglePrincipalAttestationStore.PolicyHash);
        var reopened = await Store().GetAsync(Scope, Principal);
        reopened.Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        reopened.AuditTrail.Select(item => item.Action).Should().Equal("DeclarationEnabled", "PreparerAssertionRecorded");
    }

    [Fact]
    public async Task StaleDeclarationAndChangedEvidence_DoNotAppendSuccessOrAudit()
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        await AssertDenied("stale_declaration", () => store.AttestAsync(Scope, Principal, Acknowledgment(0)));
        await AssertDenied("stale_subject", () => store.AttestAsync(Scope, Principal,
            Acknowledgment() with { SubjectHash = "old-bytes" }));
        await AssertDenied("stale_subject", () => store.AttestAsync(Scope, Principal,
            Acknowledgment() with { SubjectRevision = "revision-0" }));
        var state = await store.GetAsync(Scope, Principal);
        state.Receipts.Should().BeEmpty();
        state.AuditTrail.Should().ContainSingle();
    }

    [Theory]
    [InlineData("ATTEST-DRAFT-CHECK")]
    [InlineData("corporate-action-post")]
    [InlineData("reconciliation-waive")]
    [InlineData("report-release")]
    [InlineData("unknown-action")]
    public async Task OnlyRegisteredEvidenceActionIsEligible(string actionId)
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        await AssertDenied("excluded_action", () => store.AttestAsync(Scope, Principal,
            Acknowledgment() with { ActionId = actionId }));
        (await store.GetAsync(Scope, Principal)).Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingSupportOrUnresolvedChecks_CannotBeCuredByConfirmation()
    {
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        await AssertDenied("unsupported_subject", () => Store(resolver: (_, _, _, _) =>
            Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(null)).AttestAsync(Scope, Principal, Acknowledgment()));
        await AssertDenied("unsupported_subject", () => Store(resolver: (_, _, _, _) =>
            Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(Subject with { Eligible = false }))
            .AttestAsync(Scope, Principal, Acknowledgment()));
        await AssertDenied("unsupported_subject", () => Store(resolver: (_, _, _, _) =>
            Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(Subject with { Checks = [] }))
            .AttestAsync(Scope, Principal, Acknowledgment()));
    }

    [Fact]
    public async Task MutationsRequireHumanOrigin_AndAttestationRequiresItsOwnGrant()
    {
        var store = Store();
        await AssertDenied("human_required", () => store.ChangeDeclarationAsync(Scope,
            Principal with { Origin = OperationsActionOriginDto.AutomationAssistant }, Declaration()));
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        await AssertDenied("human_required", () => store.AttestAsync(Scope,
            Principal with { Origin = OperationsActionOriginDto.AssistantDraft }, Acknowledgment()));
        await AssertDenied("attestation_permission", () => store.AttestAsync(Scope,
            Principal with { CanAttest = false }, Acknowledgment()));
        await AssertDenied("attestation_permission", () => store.AttestAsync(Scope,
            Principal with { CanModifyTarget = false }, Acknowledgment()));
        await AssertDenied("confirmation_required", () => store.AttestAsync(Scope, Principal,
            Acknowledgment() with { Confirmation = "Approve this" }));
    }

    [Fact]
    public async Task WithdrawalBlocksPendingAndReenabledStaleCommands_ButExactRetryReturnsOriginalHistory()
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        var originalCommand = Acknowledgment();
        var receipt = await store.AttestAsync(Scope, Principal, originalCommand);
        var pending = Acknowledgment();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration(1, false));
        await AssertDenied("declaration_required", () => store.AttestAsync(Scope, Principal, pending));
        var disabled = Store(enabled: false, resolver: (_, _, _, _) => throw new InvalidOperationException("Replay must not reread source"));
        (await disabled.AttestAsync(Scope, Principal with { CanAttest = false, CanModifyTarget = false }, originalCommand))
            .Should().BeEquivalentTo(receipt);
        await AssertDenied("command_conflict", () => store.AttestAsync(Scope, Principal,
            originalCommand with { Reason = "different request under same key" }));
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration(2));
        await AssertDenied("stale_declaration", () => store.AttestAsync(Scope, Principal, pending));
        var state = await store.GetAsync(Scope, Principal);
        state.Receipts.Should().ContainSingle();
        state.DeclarationHistory.Select(item => item.Revision).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task HistoricalReplayRequiresCurrentReadPermission_AndOriginalActor()
    {
        var store = Store();
        var declarationCommand = Declaration();
        await store.ChangeDeclarationAsync(Scope, Principal, declarationCommand);
        var command = Acknowledgment();
        await store.AttestAsync(Scope, Principal, command);
        await AssertDenied("read_permission", () => store.AttestAsync(Scope,
            Principal with { CanRead = false }, command));
        await AssertDenied("read_permission", () => store.ChangeDeclarationAsync(Scope,
            Principal with { CanRead = false }, declarationCommand));
        await AssertDenied("command_conflict", () => store.AttestAsync(Scope,
            Principal with { Actor = "another-human" }, command));
    }

    [Fact]
    public async Task RevocationAfterWithdrawal_AppendsHistoryWithoutChangingOriginalReceipt()
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        var receipt = await store.AttestAsync(Scope, Principal, Acknowledgment());
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration(1, false));
        var revoke = new RevokePreparerAttestationRequestDto(Guid.NewGuid(), receipt.ReceiptId, 1, "Found an error in my assertion");
        await AssertDenied("revocation_permission", () => store.RevokeAsync(Scope,
            Principal with { Actor = "other-human" }, revoke));
        await AssertDenied("revocation_permission", () => store.RevokeAsync(Scope,
            Principal with { CanModifyTarget = false }, revoke));
        var revocation = await store.RevokeAsync(Scope, Principal, revoke);
        (await Store().RevokeAsync(Scope, Principal, revoke)).Should().BeEquivalentTo(revocation);
        await AssertDenied("stale_receipt", () => store.RevokeAsync(Scope, Principal, revoke with { CommandId = Guid.NewGuid() }));
        var reopened = await store.GetAsync(Scope, Principal);
        reopened.Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        reopened.Revocations.Should().ContainSingle();
        reopened.Declaration!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task TenantCompanyAndBookScopes_DoNotShareDeclarationsOrCommandReplay()
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        foreach (var foreign in new[]
                 {
                     Scope with { TenantId = "tenant-b" }, Scope with { CompanyId = "company-b" },
                     Scope with { LedgerBookId = Guid.NewGuid() }
                 })
        {
            (await store.GetAsync(foreign, Principal)).Declaration.Should().BeNull();
            await AssertDenied("declaration_required", () => store.AttestAsync(foreign, Principal, Acknowledgment()));
        }
    }

    [Fact]
    public async Task AtomicPublicationFailure_LeavesPriorDeclarationAndAuditUntouched()
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        var path = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        var before = await File.ReadAllTextAsync(path);
        var broken = Store(writer: (_, _, _) => throw new IOException("simulated disk failure"));
        await Assert.ThrowsAsync<IOException>(() => broken.AttestAsync(Scope, Principal, Acknowledgment()));
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        var state = await store.GetAsync(Scope, Principal);
        state.Receipts.Should().BeEmpty();
        state.AuditTrail.Should().ContainSingle();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task CorruptHistoryIsNeverTreatedAsEmptyOrOverwritten(string corruption)
    {
        var store = Store();
        await store.ChangeDeclarationAsync(Scope, Principal, Declaration());
        var path = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        await File.WriteAllTextAsync(path, corruption);
        await AssertDenied("state_unavailable", () => Store().GetAsync(Scope, Principal));
        await AssertDenied("state_unavailable", () => Store().ChangeDeclarationAsync(Scope, Principal, Declaration()));
        (await File.ReadAllTextAsync(path)).Should().Be(corruption);
    }

    [Fact]
    public async Task TwoInstancesSerializeAttestationAndWithdrawal_AndRetainOneCommitOrder()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        var writing = Store(resolver: async (_, _, _, ct) =>
        {
            entered.SetResult();
            await finish.Task.WaitAsync(ct);
            return Subject;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var acknowledgment = writing.AttestAsync(Scope, Principal, Acknowledgment(), timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var withdrawal = Store().ChangeDeclarationAsync(Scope, Principal, Declaration(1, false), timeout.Token);
        finish.SetResult();
        await Task.WhenAll(acknowledgment, withdrawal);
        var state = await Store().GetAsync(Scope, Principal);
        state.Receipts.Should().ContainSingle();
        state.Declaration!.Enabled.Should().BeFalse();
        state.AuditTrail.Select(item => item.Action).Should().Equal(
            "DeclarationEnabled", "PreparerAssertionRecorded", "DeclarationWithdrawn");
        await AssertDenied("declaration_required", () => Store().AttestAsync(Scope, Principal, Acknowledgment()));
    }

    [Fact]
    public async Task ConcurrentIdenticalCommandsAcrossInstances_WriteOneReceiptAndAudit()
    {
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        var command = Acknowledgment();
        var results = await Task.WhenAll(Store().AttestAsync(Scope, Principal, command), Store().AttestAsync(Scope, Principal, command));
        results[0].Should().BeEquivalentTo(results[1]);
        var state = await Store().GetAsync(Scope, Principal);
        state.Receipts.Should().ContainSingle();
        state.AuditTrail.Should().HaveCount(2);
    }

    private static async Task AssertDenied(string code, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<SinglePrincipalAttestationException>(action);
        exception.Code.Should().Be(code);
    }
}
