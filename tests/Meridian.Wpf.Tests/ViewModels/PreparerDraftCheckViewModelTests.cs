using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.CashFlows;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class PreparerDraftCheckViewModelTests
{
    [Fact]
    public async Task ExactRetainedRunAndOriginalPreparerAreRequired()
    {
        var fixture = new Fixture();
        var vm = await fixture.OpenAsync();
        vm.Reason = "Inspected retained terms and deterministic output";
        vm.RecordCommand.CanExecute(null).Should().BeFalse();
        vm.IsConfirmed = true;
        await vm.RecordAsync();
        var command = fixture.Records.Should().ContainSingle().Which;
        command.SubjectId.Should().Be(fixture.Snapshot.SubjectId);
        command.SubjectRevision.Should().Be(fixture.Snapshot.Revision);
        command.SubjectHash.Should().Be(fixture.Snapshot.Hash);
        command.ActionId.Should().Be(SinglePrincipalAttestationPolicy.DraftCheck);
        command.Confirmation.Should().Be(SinglePrincipalAttestationPolicy.DraftConfirmation);
        command.Origin.Should().Be(OperationsActionOriginDto.HumanOperator);
        fixture.Inspections.Should().ContainSingle().Which.Should().Be((fixture.SecurityId, fixture.RunId));
        vm.IsConfirmed.Should().BeFalse();
        vm.RecordCommand.CanExecute(null).Should().BeFalse("a successful command requires inspection of its retained history");
    }

    [Theory]
    [InlineData("foreign-preparer")]
    [InlineData("foreign-target")]
    [InlineData("unknown-confirmation")]
    [InlineData("not-eligible")]
    public async Task UnknownOrForeignSnapshotCannotBecomeADraftCheck(string defect)
    {
        var fixture = new Fixture();
        fixture.Snapshot = defect switch
        {
            "foreign-preparer" => fixture.Snapshot with { PreparedBy = "different-operator" },
            "foreign-target" => fixture.Snapshot with { SubjectId = $"Security/{Guid.NewGuid():D}/{fixture.RunId:D}" },
            "unknown-confirmation" => fixture.Snapshot with { Confirmation = "Unknown confirmation" },
            _ => fixture.Snapshot with { Eligible = false }
        };
        var vm = await fixture.OpenAsync();
        vm.Reason = "Inspected";
        vm.IsConfirmed = true;
        await vm.RecordAsync();
        vm.RecordCommand.CanExecute(null).Should().BeFalse();
        fixture.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task DraftCheckRequiresItsOwnGrantAndV2DeclarationReaffirmation()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with { CanAttest = true, CanAttestDrafts = false };
        var vm = await fixture.OpenAsync();
        vm.Reason = "Inspected";
        vm.IsConfirmed = true;
        vm.RecordCommand.CanExecute(null).Should().BeFalse();
        fixture.Workspace = fixture.Workspace with
        {
            CanAttestDrafts = true,
            State = fixture.Workspace.State with { Declaration = fixture.Workspace.State.Declaration! with { PolicyVersion = SinglePrincipalAttestationPolicy.Version } }
        };
        await vm.InspectAsync();
        vm.IsConfirmed = true;
        vm.RecordCommand.CanExecute(null).Should().BeFalse("v1 evidence declarations do not authorize a v2 draft check");
    }

    [Fact]
    public async Task UncertainRecordRetriesExactOriginalCommandAndOperands()
    {
        var fixture = new Fixture();
        fixture.RecordResponse = ApiResponse<PreparerAttestationReceiptDto>.Fail("Response interrupted", 0, true);
        var vm = await fixture.OpenAsync();
        vm.Reason = "Original inspected run";
        vm.IsConfirmed = true;
        await vm.RecordAsync();
        vm.HasPendingCommand.Should().BeTrue();
        vm.CanEdit.Should().BeFalse();
        fixture.Snapshot = fixture.Snapshot with { Hash = "changed-current-hash" };
        await vm.InspectAsync();
        fixture.RecordResponse = ApiResponse<PreparerAttestationReceiptDto>.Ok(fixture.Receipt);
        await vm.RetryAsync();
        fixture.Records.Should().HaveCount(2);
        fixture.Records[1].Should().Be(fixture.Records[0]);
        fixture.Records[1].SubjectHash.Should().NotBe(fixture.Snapshot.Hash);
        vm.HasPendingCommand.Should().BeFalse();
    }

    [Fact]
    public async Task OwnCorrectiveRevocationSurvivesDisabledPolicyLostRecordGrantAndMissingSource()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with
        {
            CanAttest = false, CanAttestDrafts = false, CanRevoke = false, CanRevokeDrafts = true,
            State = fixture.Workspace.State with
            {
                PolicyEnabled = false,
                Declaration = fixture.Workspace.State.Declaration! with { Enabled = false },
                Receipts = [fixture.Receipt]
            }
        };
        fixture.SnapshotResponse = ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Fail("Retained source unavailable", 404);
        var vm = await fixture.OpenAsync();
        vm.History.Should().ContainSingle().Which.Should().Contain(SinglePrincipalAttestationPolicy.Disclosure);
        vm.SelectedReceipt = vm.RevocableReceipts.Single();
        vm.RevocationReason = "Correcting my retained preparer assertion";
        vm.RevokeCommand.CanExecute(null).Should().BeTrue();
        await vm.RevokeAsync();
        fixture.Revocations.Should().ContainSingle().Which.ReceiptId.Should().Be(fixture.Receipt.ReceiptId);
        fixture.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task ForeignAndUnknownHistoryNeverOffersOwnCorrectiveRevocation()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with
        {
            State = fixture.Workspace.State with { Receipts = [fixture.Receipt with { AttestedBy = "other-operator" }, fixture.Receipt with { ReceiptId = Guid.NewGuid(), SchemaVersion = 999 }] }
        };
        var vm = await fixture.OpenAsync();
        vm.History.Should().HaveCount(2);
        vm.History.Should().Contain(item => item.Contains("Review classification unavailable"));
        vm.RevocableReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokedReceiptRemainsVisibleAndCannotBeRevokedAgain()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with
        {
            State = fixture.Workspace.State with
            {
                Receipts = [fixture.Receipt],
                Revocations = [new(Guid.NewGuid(), fixture.Receipt.ReceiptId, 2, fixture.Actor, DateTimeOffset.UtcNow, "Wrong retained assumptions")]
            }
        };
        var vm = await fixture.OpenAsync();
        vm.History.Should().ContainSingle().Which.Should().Contain("Original record retained").And.Contain("Wrong retained assumptions");
        vm.RevocableReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task LateInspectionCannotAttachToANewRetainedTarget()
    {
        var fixture = new Fixture();
        var vm = await fixture.OpenAsync();
        var response = new TaskCompletionSource<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>>();
        fixture.SnapshotTask = response.Task;
        var inspect = vm.InspectAsync();
        vm.SetRetainedTarget(fixture.SecurityId, Guid.NewGuid());
        response.SetResult(ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Ok(fixture.Snapshot));
        await inspect;
        vm.SnapshotText.Should().BeEmpty();
        vm.Checks.Should().BeEmpty();
        vm.RecordCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StaleMutationRequiresNewInspectionAndNewConfirmation()
    {
        var fixture = new Fixture();
        fixture.RecordResponse = ApiResponse<PreparerAttestationReceiptDto>.Fail("Source revision changed", 409);
        var vm = await fixture.OpenAsync();
        vm.Reason = "Inspected original";
        vm.IsConfirmed = true;
        await vm.RecordAsync();
        vm.HasPendingCommand.Should().BeFalse();
        vm.IsConfirmed.Should().BeFalse();
        vm.SnapshotText.Should().BeEmpty();
        vm.RecordCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class Fixture : IPreparerDraftCheckApiClient
    {
        public Guid SecurityId { get; } = Guid.NewGuid();
        public Guid RunId { get; } = Guid.NewGuid();
        public string Actor => "principal";
        public SinglePrincipalAttestationWorkspaceDto Workspace { get; set; }
        public SinglePrincipalAttestationSubjectSnapshotDto Snapshot { get; set; }
        public PreparerAttestationReceiptDto Receipt { get; }
        public List<(Guid SecurityId, Guid RunId)> Inspections { get; } = [];
        public List<RecordPreparerAttestationRequestDto> Records { get; } = [];
        public List<RevokePreparerAttestationRequestDto> Revocations { get; } = [];
        public ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>? SnapshotResponse { get; set; }
        public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>>? SnapshotTask { get; set; }
        public ApiResponse<PreparerAttestationReceiptDto>? RecordResponse { get; set; }
        public Fixture()
        {
            var scope = new SinglePrincipalAttestationScopeDto("tenant", "company");
            var declaration = new SinglePrincipalDeclarationDto(Guid.NewGuid(), 3, scope, true, Actor,
                SinglePrincipalAttestationPolicy.DraftVersion, "policy-hash", Actor, DateTimeOffset.UtcNow, "Reviewed declaration");
            Workspace = new(new(scope, true, SinglePrincipalAttestationPolicy.DraftVersion, "policy-hash", SinglePrincipalAttestationPolicy.Disclosure,
                SinglePrincipalAttestationPolicy.Confirmation, [SinglePrincipalAttestationPolicy.EvidenceAcknowledgment, SinglePrincipalAttestationPolicy.DraftCheck],
                declaration, [declaration], [], [], []), Actor, true, true, true, true, true);
            Snapshot = new("contractual-cashflow-draft", $"Security/{SecurityId:D}/{RunId:D}", "revision-1", "retained-hash", "retained-run", "source-hash",
                "importer-is-not-preparer", "Retained deterministic cash-flow calculation", true, ["Non-postable", "Output hash retained"],
                PreparedBy: Actor, Confirmation: SinglePrincipalAttestationPolicy.DraftConfirmation);
            Receipt = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SinglePrincipalAttestationPolicy.SchemaVersion,
                SinglePrincipalAttestationPolicy.DecisionKind, SinglePrincipalAttestationPolicy.Disclosure, scope, SinglePrincipalAttestationPolicy.DraftCheck,
                Snapshot.SubjectKind, Snapshot.SubjectId, Snapshot.Revision, Snapshot.Hash, Snapshot.SourceArtifactId, Snapshot.SourceHash,
                Snapshot.SourceActor, Snapshot.SourceProvenance, Actor, Actor, OperationsActionOriginDto.HumanOperator, declaration.DeclarationId,
                declaration.Revision, declaration.PolicyVersion, declaration.PolicyHash, DateTimeOffset.UtcNow, "Inspected", SinglePrincipalAttestationPolicy.DraftConfirmation, Snapshot.Checks);
        }
        public async Task<PreparerDraftCheckViewModel> OpenAsync()
        {
            var vm = new PreparerDraftCheckViewModel(this);
            vm.SetRetainedTarget(SecurityId, RunId);
            await vm.InspectAsync();
            return vm;
        }
        public Task<ApiResponse<SinglePrincipalAttestationWorkspaceDto>> GetDraftCheckWorkspaceAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Ok(Workspace));
        public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> InspectDraftCheckAsync(Guid securityId, Guid runId, CancellationToken ct = default)
        {
            Inspections.Add((securityId, runId));
            return SnapshotTask ?? Task.FromResult(SnapshotResponse ?? ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Ok(Snapshot));
        }
        public Task<ApiResponse<PreparerAttestationReceiptDto>> RecordDraftCheckAsync(RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        {
            Records.Add(request);
            return Task.FromResult(RecordResponse ?? ApiResponse<PreparerAttestationReceiptDto>.Ok(Receipt));
        }
        public Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeDraftCheckAsync(RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        {
            Revocations.Add(request);
            return Task.FromResult(ApiResponse<PreparerAttestationRevocationDto>.Ok(new(request.CommandId, request.ReceiptId, 2, Actor, DateTimeOffset.UtcNow, request.Reason)));
        }
    }
}
