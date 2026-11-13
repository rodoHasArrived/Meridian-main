using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed partial class EvidenceWorkbenchViewModelTests
{
    [Fact]
    public async Task AttestationActions_RequireConfiguredPolicyAndExplicitGrants()
    {
        var client = CreateLoadedClient();
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(CreateAttestationWorkspace());
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.DeclarationReason = "One principal controls this scope";
        vm.AttestationReason = "Inspected retained source";
        vm.IsAttestationConfirmed = true;

        vm.ToggleAttestationDeclarationCommand.CanExecute(null).Should().BeFalse();
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        await vm.ChangeAttestationDeclarationAsync();
        await vm.RecordAttestationAsync();
        client.DeclarationRequests.Should().BeEmpty();
        client.AttestationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AttestationActions_ConfirmExactServerSnapshotWithoutReplacingSourceImporter()
    {
        var client = CreateAttestationActionClient();
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        await vm.PreviewAttestationCommand.ExecuteAsync(null);
        vm.AttestationPreviewText.Should().Contain("external-importer").And.Contain("Synthetic");
        vm.AttestationReason = "Inspected exact retained source and checks";
        vm.IsAttestationConfirmed = true;
        vm.RecordAttestationCommand.CanExecute(null).Should().BeTrue();
        await vm.RecordAttestationCommand.ExecuteAsync(null);

        var request = client.AttestationRequests.Should().ContainSingle().Which;
        request.SubjectId.Should().Be(client.AttestationSubjectResponse.Data!.SubjectId);
        request.SubjectRevision.Should().Be("revision-seven");
        request.SubjectHash.Should().Be("subject-hash-seven");
        request.ExpectedDeclarationRevision.Should().Be(3);
        request.Confirmation.Should().Be(SinglePrincipalAttestationPolicy.Confirmation);
        request.ActionId.Should().Be(SinglePrincipalAttestationPolicy.EvidenceAcknowledgment);
        vm.IsAttestationConfirmed.Should().BeFalse();
        vm.HasPendingAttestationCommand.Should().BeFalse();
    }

    [Fact]
    public async Task AttestationActions_DocumentChangeDiscardsPreviewAndConfirmation()
    {
        var client = CreateAttestationActionClient();
        var first = client.DocumentsResponse.Data!.Single();
        client.DocumentsResponse = ApiResponse<EvidenceVaultDocumentEntryDto[]>.Ok(
            [first, first with { Document = first.Document with { DocumentId = "other-document" } }]);
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.First();
        await vm.PreviewAttestationAsync();
        vm.IsAttestationConfirmed = true;
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Last();

        vm.IsAttestationConfirmed.Should().BeFalse();
        vm.AttestationChecks.Should().BeEmpty();
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task AttestationActions_ForeignSnapshotCannotBeConfirmed()
    {
        var client = CreateAttestationActionClient();
        client.AttestationSubjectResponse = ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Ok(
            client.AttestationSubjectResponse.Data! with { SubjectId = "foreign-vault/document" });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        await vm.PreviewAttestationAsync();
        vm.AttestationReason = "Checked";
        vm.IsAttestationConfirmed = true;

        vm.AttestationActionStatus.Should().Contain("does not match");
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        client.AttestationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AttestationActions_LatePreviewCannotReplaceRefreshedDocumentContext()
    {
        var client = CreateAttestationActionClient();
        var completion = new TaskCompletionSource<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>>();
        client.AttestationSubjectTask = completion.Task;
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        var preview = vm.PreviewAttestationAsync();
        await vm.RefreshAsync();
        completion.SetResult(client.AttestationSubjectResponse);
        await preview;

        vm.SelectedAttestationDocument.Should().BeNull();
        vm.AttestationChecks.Should().BeEmpty();
        vm.AttestationPreviewText.Should().Contain("No exact source snapshot");
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task AttestationActions_UncertainResponseRetriesExactCommandIdentityAndOperands()
    {
        var client = CreateAttestationActionClient();
        client.RecordHandler = _ => ApiResponse<PreparerAttestationReceiptDto>.Fail("Connection interrupted", 0, true);
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        await vm.PreviewAttestationAsync();
        vm.AttestationReason = "Inspected the source";
        vm.IsAttestationConfirmed = true;
        await vm.RecordAttestationAsync();
        vm.HasPendingAttestationCommand.Should().BeTrue();
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        client.RecordHandler = _ => ApiResponse<PreparerAttestationReceiptDto>.Ok(client.AttestationsResponse.Data!.State.Receipts.Single());
        await vm.RetryAttestationAsync();

        client.AttestationRequests.Should().HaveCount(2);
        client.AttestationRequests[1].Should().BeEquivalentTo(client.AttestationRequests[0]);
        vm.HasPendingAttestationCommand.Should().BeFalse();
    }

    [Fact]
    public async Task AttestationActions_LateMutationDoesNotReplaceNewPrincipalContextOrDraftText()
    {
        var client = CreateAttestationActionClient();
        var completion = new TaskCompletionSource<ApiResponse<PreparerAttestationReceiptDto>>();
        client.AttestationRecordTask = completion.Task;
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        await vm.PreviewAttestationAsync();
        vm.AttestationReason = "Original preparer source inspection";
        vm.IsAttestationConfirmed = true;
        var originalReceipt = client.AttestationsResponse.Data!.State.Receipts.Single();
        var mutation = vm.RecordAttestationAsync();
        var prior = client.AttestationsResponse.Data!;
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(prior with
        {
            Actor = "new-principal",
            State = prior.State with { Scope = new("new-tenant", "new-company"), Receipts = [], Declaration = null }
        });
        await vm.RefreshAsync();
        vm.AttestationReason = "New context draft text";
        completion.SetResult(ApiResponse<PreparerAttestationReceiptDto>.Ok(originalReceipt));
        await mutation;

        vm.AttestationScopeText.Should().Contain("new-company");
        vm.AttestationRows.Should().BeEmpty();
        vm.AttestationReason.Should().Be("New context draft text");
        vm.AttestationActionStatus.Should().Contain("original scope").And.Contain("current context was preserved");
    }

    [Fact]
    public async Task AttestationActions_DisposedViewCannotInvokeCommandsOrReadDisposedToken()
    {
        var client = CreateAttestationActionClient();
        var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.Dispose();
        foreach (var command in new[] { vm.PreviewAttestationCommand, vm.RecordAttestationCommand,
                     vm.ToggleAttestationDeclarationCommand, vm.RevokeAttestationCommand, vm.RetryAttestationCommand, vm.ExportAttestationProofCommand })
        {
            command.CanExecute(null).Should().BeFalse();
            await command.ExecuteAsync(null);
        }
        client.AttestationRequests.Should().BeEmpty();
        client.DeclarationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AttestationActions_StaleRefusalRequiresFreshInspection()
    {
        var client = CreateAttestationActionClient();
        client.RecordHandler = _ => ApiResponse<PreparerAttestationReceiptDto>.Fail("Declaration changed", 409);
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        await vm.PreviewAttestationAsync();
        vm.AttestationReason = "Inspected the source";
        vm.IsAttestationConfirmed = true;
        await vm.RecordAttestationAsync();

        vm.HasPendingAttestationCommand.Should().BeFalse();
        vm.IsAttestationConfirmed.Should().BeFalse();
        vm.SelectedAttestationDocument.Should().BeNull();
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        vm.AttestationActionStatus.Should().Contain("refused");
    }

    [Fact]
    public async Task AttestationActions_WithdrawalAndCorrectiveSelfRevocationRemainAvailableWithPolicyOff()
    {
        var client = CreateAttestationActionClient();
        var workspace = client.AttestationsResponse.Data!;
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(
            workspace with { State = workspace.State with { PolicyEnabled = false } });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.DeclarationReason = "Withdraw prospectively";
        vm.ToggleAttestationDeclarationCommand.CanExecute(null).Should().BeTrue();
        await vm.ChangeAttestationDeclarationAsync();
        client.DeclarationRequests.Should().ContainSingle().Which.Enabled.Should().BeFalse();
        vm.SelectedAttestationReceipt = vm.AttestationRows.Single();
        vm.AttestationRevocationReason = "Original preparer assertion was erroneous";
        vm.RevokeAttestationCommand.CanExecute(null).Should().BeTrue();
        await vm.RevokeAttestationAsync();
        client.RevocationRequests.Should().ContainSingle();
        vm.AttestationRows.Should().ContainSingle("corrective revocation does not delete the original record");
    }

    [Fact]
    public async Task AttestationActions_ReaffirmExistingDeclarationWhenPolicyChanges()
    {
        var client = CreateAttestationActionClient();
        var workspace = client.AttestationsResponse.Data!;
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(workspace with
        {
            State = workspace.State with
            {
                PolicyVersion = SinglePrincipalAttestationPolicy.DraftVersion,
                Declaration = workspace.State.Declaration! with { PolicyVersion = SinglePrincipalAttestationPolicy.Version }
            }
        });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.DeclarationReason = "Reviewed the current explicitly offered policy";
        vm.DeclarationActionText.Should().Contain("Reaffirm");
        vm.SelectedAttestationDocument = vm.AttestationDocuments.Single();
        await vm.PreviewAttestationAsync();
        vm.AttestationReason = "A previous-policy declaration cannot authorize this assertion";
        vm.IsAttestationConfirmed = true;
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse();
        await vm.ChangeAttestationDeclarationAsync();
        var command = client.DeclarationRequests.Should().ContainSingle().Which;
        command.Enabled.Should().BeTrue();
        command.PolicyVersion.Should().Be(SinglePrincipalAttestationPolicy.DraftVersion);
        command.ExpectedRevision.Should().Be(3);
    }

    [Fact]
    public async Task AttestationActions_EnableWithdrawnDeclarationForCurrentAuthorizedPrincipal()
    {
        var client = CreateAttestationActionClient();
        var workspace = client.AttestationsResponse.Data!;
        var withdrawn = workspace.State.Declaration! with { Enabled = false, PrincipalId = "previous-principal", Revision = 4 };
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(workspace with
        {
            State = workspace.State with { Declaration = withdrawn, DeclarationHistory = [withdrawn] }
        });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.DeclarationActionText.Should().Be("Enable declaration for myself");
        vm.ToggleAttestationDeclarationCommand.CanExecute(null).Should().BeFalse("an explicit reason is required");
        vm.DeclarationReason = "Declare responsibility for this scope after the prior principal withdrew";
        vm.IsAttestationConfirmed = true;
        vm.RecordAttestationCommand.CanExecute(null).Should().BeFalse("historical ownership does not authorize an assertion");
        vm.ToggleAttestationDeclarationCommand.CanExecute(null).Should().BeTrue();

        await vm.ToggleAttestationDeclarationCommand.ExecuteAsync(null);

        var command = client.DeclarationRequests.Should().ContainSingle().Which;
        command.Enabled.Should().BeTrue();
        command.PrincipalId.Should().Be(workspace.Actor).And.NotBe(withdrawn.PrincipalId);
        command.ExpectedRevision.Should().Be(withdrawn.Revision);
        command.PolicyVersion.Should().Be(workspace.State.PolicyVersion);
        command.Reason.Should().Be("Declare responsibility for this scope after the prior principal withdrew");
        client.AttestationRequests.Should().BeEmpty("enabling a declaration does not attest any source");
        vm.IsAttestationConfirmed.Should().BeFalse("a declaration change requires a fresh source confirmation");
    }

    [Theory]
    [InlineData("active-foreign-declaration")]
    [InlineData("no-administration-grant")]
    [InlineData("policy-disabled")]
    [InlineData("unknown-policy")]
    public async Task AttestationActions_DeclarationReassignmentPreservesCurrentAuthorityChecks(string blocker)
    {
        var client = CreateAttestationActionClient();
        var workspace = client.AttestationsResponse.Data!;
        var declaration = workspace.State.Declaration! with
        {
            Enabled = blocker == "active-foreign-declaration", PrincipalId = "previous-principal"
        };
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(workspace with
        {
            CanAdministerDeclaration = blocker != "no-administration-grant",
            State = workspace.State with
            {
                Declaration = declaration,
                PolicyEnabled = blocker != "policy-disabled",
                PolicyVersion = blocker == "unknown-policy" ? "unsupported-policy" : workspace.State.PolicyVersion
            }
        });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.DeclarationReason = "Attempt to bind the current principal";

        vm.ToggleAttestationDeclarationCommand.CanExecute(null).Should().BeFalse();
        await vm.ChangeAttestationDeclarationAsync();
        client.DeclarationRequests.Should().BeEmpty();
        client.AttestationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AttestationActions_OriginalProofRemainsReadableAfterWithdrawalAndRevocation()
    {
        var client = CreateLoadedClient();
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(CreateAttestationWorkspace(revoked: true));
        const string original = "{\"classification\":\"PreparerAttestation\",\"originalDeclarationRevision\":3}";
        client.AttestationProofResponse = ApiResponse<string>.Ok(original);
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();
        vm.SelectedAttestationReceipt = vm.AttestationRows.Single();
        await vm.ReadAttestationProofExportAsync();
        vm.AttestationExportText.Should().Be(original);
        vm.AttestationRows.Single().CurrentStatus.Should().Contain("Revoked");
        vm.AttestationActionStatus.Should().Contain("separate");
    }

    [Fact]
    public async Task PacketAttestations_ShowTaskCountsAndCanonicalDisclosureWithoutChangingReadiness()
    {
        var client = CreateAttestationActionClient();
        var workspace = client.AttestationsResponse.Data!;
        var projection = new PreparerAttestationEvidenceProjectionDto(workspace.State.Scope, 1, "Available", DateTimeOffset.UtcNow,
            SinglePrincipalAttestationPolicy.Disclosure, "Evidence-task receipts, not financial approval", 1, 1, 0, 1, 0,
            [new(workspace.State.Receipts.Single(), workspace.State.Declaration!, null, "ExactSource")], []);
        var original = client.PacketResponse.Data!;
        client.PacketResponse = ApiResponse<EvidencePacketDto>.Ok(original with { PreparerAttestations = projection });
        using var vm = new EvidenceWorkbenchViewModel(client);
        await vm.RefreshAsync();

        vm.PacketAttestationSummaryText.Should().Contain("receipts: 1").And.Contain("not financial approvals or Verified Coverage");
        vm.PacketAttestationDisclosures.Should().ContainSingle().Which.Should().Contain(SinglePrincipalAttestationPolicy.Disclosure);
        vm.CompletenessStatusText.Should().Be("Review Required");
        var unavailable = projection with { SchemaVersion = 999 };
        PreparerAttestationEvidencePresentation.Describe(unavailable, out var summary).Should().BeEmpty();
        summary.Should().Contain("unavailable").And.NotContain("receipts: 0");
        PreparerAttestationEvidencePresentation.MatchesScope(projection, "foreign-tenant", "company-1", null).Should().BeFalse();
        PreparerAttestationEvidencePresentation.MatchesScope(projection, "tenant-1", "company-1", Guid.NewGuid()).Should().BeTrue(
            "a same-company projection without a book remains a company-level evidence assessment");
        PreparerAttestationEvidencePresentation.MatchesScope(projection with { Scope = projection.Scope with { LedgerBookId = Guid.NewGuid() } },
            "tenant-1", "company-1", Guid.NewGuid()).Should().BeFalse();
    }

    private static FakeEvidenceWorkbenchApiClient CreateAttestationActionClient()
    {
        var client = CreateLoadedClient();
        var old = CreateAttestationWorkspace();
        var declaration = old.State.Declaration! with { Enabled = true, Revision = 3 };
        var workspace = old with
        {
            CanAdministerDeclaration = true, CanAttest = true, CanRevoke = true,
            State = old.State with
            {
                PolicyEnabled = true, AllowedActions = [SinglePrincipalAttestationPolicy.EvidenceAcknowledgment],
                Declaration = declaration, DeclarationHistory = [declaration]
            }
        };
        client.AttestationsResponse = ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(workspace);
        var document = client.DocumentsResponse.Data!.Single();
        client.AttestationSubjectResponse = ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Ok(new(
            "evidence-document", $"{document.VaultId}/{document.Document.DocumentId}", "revision-seven", "subject-hash-seven",
            "artifact-seven", "source-hash-seven", "external-importer", "Synthetic", true, ["Retained source bytes verified"]));
        client.RecordHandler = _ => ApiResponse<PreparerAttestationReceiptDto>.Ok(workspace.State.Receipts.Single());
        return client;
    }

    private sealed partial class FakeEvidenceWorkbenchApiClient
    {
        public ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto> AttestationSubjectResponse { get; set; } =
            ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Fail("Preview unavailable", 503);
        public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>>? AttestationSubjectTask { get; set; }
        public List<ChangeSinglePrincipalDeclarationRequestDto> DeclarationRequests { get; } = [];
        public List<RecordPreparerAttestationRequestDto> AttestationRequests { get; } = [];
        public List<RevokePreparerAttestationRequestDto> RevocationRequests { get; } = [];
        public Func<RecordPreparerAttestationRequestDto, ApiResponse<PreparerAttestationReceiptDto>>? RecordHandler { get; set; }
        public Task<ApiResponse<PreparerAttestationReceiptDto>>? AttestationRecordTask { get; set; }
        public ApiResponse<string> AttestationProofResponse { get; set; } = ApiResponse<string>.Fail("Original proof unavailable", 501);
        public Task<ApiResponse<string>> ReadAttestationProofExportAsync(Guid receiptId, CancellationToken ct = default)
            => Task.FromResult(AttestationProofResponse);
        public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> GetAttestationSubjectAsync(string vaultId, string documentId, CancellationToken ct = default)
            => AttestationSubjectTask ?? Task.FromResult(AttestationSubjectResponse);
        public Task<ApiResponse<SinglePrincipalDeclarationDto>> ChangeAttestationDeclarationAsync(ChangeSinglePrincipalDeclarationRequestDto request, CancellationToken ct = default)
        {
            DeclarationRequests.Add(request);
            return Task.FromResult(ApiResponse<SinglePrincipalDeclarationDto>.Ok(AttestationsResponse.Data!.State.Declaration! with { Enabled = request.Enabled }));
        }
        public Task<ApiResponse<PreparerAttestationReceiptDto>> RecordAttestationAsync(RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        {
            AttestationRequests.Add(request);
            return AttestationRecordTask ?? Task.FromResult(RecordHandler!(request));
        }
        public Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeAttestationAsync(RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        {
            RevocationRequests.Add(request);
            return Task.FromResult(ApiResponse<PreparerAttestationRevocationDto>.Ok(new(request.CommandId, request.ReceiptId, 2,
                AttestationsResponse.Data!.Actor, DateTimeOffset.UtcNow, request.Reason)));
        }
    }
}
