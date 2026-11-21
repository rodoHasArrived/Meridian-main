using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Ui.Shared.Evidence;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SinglePrincipalDraftAttestationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-draft-attestation-" + Guid.NewGuid().ToString("N"));
    private static readonly SinglePrincipalAttestationScopeDto Scope = new("tenant-a", "company-a");
    private static readonly SinglePrincipalAttestationAuthority Human = new("preparer-a", OperationsActionOriginDto.HumanOperator,
        CanRead: true, CanAdministerDeclaration: true, CanAttest: true, CanModifyTarget: true, CanModifyDraftTarget: true);
    private const string Operands = "{\"version\":1,\"isPostable\":false,\"run\":\"synthetic\"}";
    private static readonly SinglePrincipalAttestationSubjectSnapshotDto Draft = new(
        ContractualCashFlowDraftAttestationSubjectResolver.SubjectKind, $"Security/{Guid.NewGuid():D}/{Guid.NewGuid():D}",
        Sha256Digest.ComputeUtf8(Operands), Sha256Digest.ComputeUtf8(Operands), "retained-run", Sha256Digest.ComputeUtf8("retained"),
        Human.Actor, "Unclassified", true, ["Exact non-postable run replayed"],
        ContractualCashFlowDraftAttestationSubjectResolver.SourceSubjectKind, Guid.NewGuid().ToString("D"), Operands, Human.Actor,
        SinglePrincipalAttestationPolicy.DraftConfirmation);

    private SinglePrincipalAttestationStore Store(bool draft = true, SinglePrincipalAttestationSubjectSnapshotDto? target = null) =>
        new(_root, (_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(target ?? Draft),
            policyEnabled: true, draftChecksEnabled: draft);

    private static ChangeSinglePrincipalDeclarationRequestDto Declaration(long revision, string version) =>
        new(Guid.NewGuid(), revision, true, Human.Actor, version, "One principal controls this synthetic scope");

    private static RecordPreparerAttestationRequestDto Check(long revision = 1) =>
        new(Guid.NewGuid(), revision, SinglePrincipalAttestationPolicy.DraftCheck, Draft.SubjectKind, Draft.SubjectId,
            Draft.Revision, Draft.Hash, "Inspected this exact immutable calculation", SinglePrincipalAttestationPolicy.DraftConfirmation);

    [Fact]
    public async Task DraftCheckRequiresExplicitV2Declaration_AndV1HistoryRemainsUnchanged()
    {
        var v1 = Store(draft: false);
        await v1.ChangeDeclarationAsync(Scope, Human, Declaration(0, SinglePrincipalAttestationPolicy.Version));
        var evidence = Draft with { SubjectKind = "evidence-document", SubjectId = "vault/document", PreparedBy = null };
        var ack = new RecordPreparerAttestationRequestDto(Guid.NewGuid(), 1, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment,
            evidence.SubjectKind, evidence.SubjectId, evidence.Revision, evidence.Hash, "Read retained evidence", SinglePrincipalAttestationPolicy.Confirmation);
        var receipt = await Store(draft: false, target: evidence).AttestAsync(Scope, Human, ack);
        var path = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Single();
        var before = await File.ReadAllTextAsync(path);

        var v2 = Store();
        var state = await v2.GetAsync(Scope, Human);
        state.PolicyVersion.Should().Be(SinglePrincipalAttestationPolicy.DraftVersion);
        state.Declaration!.PolicyVersion.Should().Be(SinglePrincipalAttestationPolicy.Version);
        state.Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        (await File.ReadAllTextAsync(path)).Should().Be(before, "reading a new policy cannot rewrite existing receipts or command results");
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => v2.AttestAsync(Scope, Human, Check()))).Code
            .Should().Be("unsupported_policy", "an old declaration cannot automatically acquire draft authority");

        await v2.ChangeDeclarationAsync(Scope, Human, Declaration(1, SinglePrincipalAttestationPolicy.DraftVersion));
        var checkedDraft = await v2.AttestAsync(Scope, Human, Check(2));
        checkedDraft.ActionId.Should().Be(SinglePrincipalAttestationPolicy.DraftCheck);
        checkedDraft.Confirmation.Should().Be(SinglePrincipalAttestationPolicy.DraftConfirmation);
        checkedDraft.PolicyVersion.Should().Be(SinglePrincipalAttestationPolicy.DraftVersion);
        var reopened = await Store(draft: false).GetAsync(Scope, Human);
        reopened.Receipts.Should().HaveCount(2);
        reopened.Receipts.First().Should().BeEquivalentTo(receipt);
        reopened.DeclarationHistory[0].PolicyHash.Should().Be(SinglePrincipalAttestationStore.PolicyHash);
    }

    [Fact]
    public async Task DraftChecksRequireTheirOwnTargetGrantOriginalPreparerAndConfirmation()
    {
        await Store().ChangeDeclarationAsync(Scope, Human, Declaration(0, SinglePrincipalAttestationPolicy.DraftVersion));
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => Store().AttestAsync(Scope,
            Human with { CanModifyDraftTarget = false }, Check()))).Code.Should().Be("attestation_permission");
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => Store(target: Draft with { PreparedBy = "another-preparer" })
            .AttestAsync(Scope, Human, Check()))).Code.Should().Be("preparer_mismatch");
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => Store().AttestAsync(Scope, Human,
            Check() with { Confirmation = SinglePrincipalAttestationPolicy.Confirmation }))).Code.Should().Be("confirmation_required");
        (await Store().GetAsync(Scope, Human)).Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task TurningDraftFlagOffBlocksNewChecksButKeepsHistoricalReceiptsReadable()
    {
        await Store().ChangeDeclarationAsync(Scope, Human, Declaration(0, SinglePrincipalAttestationPolicy.DraftVersion));
        var receipt = await Store().AttestAsync(Scope, Human, Check());
        (await Store(draft: false).GetAsync(Scope, Human)).Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => Store(draft: false).AttestAsync(Scope, Human, Check())))
            .Code.Should().Be("excluded_action");
        var revocation = new RevokePreparerAttestationRequestDto(Guid.NewGuid(), receipt.ReceiptId, 1, "Correcting my historical draft check");
        (await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() => Store(draft: false).RevokeAsync(Scope,
            Human with { CanModifyDraftTarget = false }, revocation))).Code.Should().Be("revocation_permission");
        await Store(draft: false).RevokeAsync(Scope, Human, revocation);
        (await Store(draft: false).GetAsync(Scope, Human)).Revocations.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolverReturnsExactScopedHumanRunWithoutChangingItsPostingAuthority()
    {
        var (run, security) = CreateRun();
        var resolver = Resolver(run, security);
        var result = await resolver.ResolveAsync(run.SubjectId, run.RunId, Scope.TenantId, Scope.CompanyId, Human.Actor);
        result.Should().NotBeNull();
        result!.PreparedBy.Should().Be(Human.Actor);
        result.Confirmation.Should().Be(SinglePrincipalAttestationPolicy.DraftConfirmation);
        result.Eligible.Should().BeTrue();
        result.SourceHash.Should().Be(Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(run, ContractualCashFlowJsonContext.Default.ContractualCashFlowRun)));
        Sha256Digest.ComputeUtf8(result.SubjectSnapshotJson!).Should().Be(result.Hash);
        run.Result.IsPostable.Should().BeFalse();
    }

    [Fact]
    public async Task MissingCalculationModulesCannotProduceDraftAuthority()
    {
        (await new ContractualCashFlowDraftAttestationSubjectResolver().ResolveAsync(
            Guid.NewGuid(), Guid.NewGuid(), Scope.TenantId, Scope.CompanyId, Human.Actor)).Should().BeNull();
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("different-preparer")]
    [InlineData("automation")]
    [InlineData("unknown-context")]
    public async Task ResolverRefusesUnboundForeignOrNonHumanDrafts(string failure)
    {
        var (run, security) = CreateRun();
        run = failure switch
        {
            "legacy" => run with { AttestationContext = null },
            "foreign-tenant" => run with { AttestationContext = run.AttestationContext! with { TenantId = "foreign" } },
            "foreign-company" => run with { AttestationContext = run.AttestationContext! with { CompanyId = "foreign" } },
            "different-preparer" => run with { Actor = "other", AttestationContext = run.AttestationContext! with { PreparedBy = "other" } },
            "automation" => run with { AttestationContext = run.AttestationContext! with { Origin = OperationsActionOriginDto.AutomationAssistant } },
            _ => run with { AttestationContext = run.AttestationContext! with { SchemaVersion = 999 } }
        };
        (await Resolver(run, security).ResolveAsync(run.SubjectId, run.RunId, Scope.TenantId, Scope.CompanyId, Human.Actor)).Should().BeNull();
    }

    [Fact]
    public async Task ChangedOrMissingSecuritySourceCannotSupplyAValidDraftCheck()
    {
        var (run, security) = CreateRun();
        (await Resolver(run, security with { Version = security.Version + 1 })
            .ResolveAsync(run.SubjectId, run.RunId, Scope.TenantId, Scope.CompanyId, Human.Actor)).Should().BeNull();
        (await Resolver(run, null).ResolveAsync(run.SubjectId, run.RunId, Scope.TenantId, Scope.CompanyId, Human.Actor)).Should().BeNull();
    }

    [Fact]
    public async Task BoundRunStorePreservesScopeAndRefusesIdentityCollisionInsteadOfReassigningPreparer()
    {
        var (run, _) = CreateRun();
        var store = new FileContractualCashFlowRunStore(new StorageOptions { RootPath = _root });
        await store.SaveAsync(run);
        var reopened = await store.GetAsync(run.SubjectKind, run.SubjectId, run.RunId);
        reopened!.AttestationContext.Should().BeEquivalentTo(run.AttestationContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(run with
        {
            Actor = "another-preparer",
            AttestationContext = run.AttestationContext! with { PreparedBy = "another-preparer" }
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(run with
        { AttestationContext = run.AttestationContext! with { CompanyId = "foreign" } }));
    }

    [Fact]
    public void LegacyRunJsonDoesNotAcquireAnImplicitAttestationBinding()
    {
        var (run, _) = CreateRun();
        var legacy = JsonSerializer.Serialize(run with { AttestationContext = null }, ContractualCashFlowJsonContext.Default.ContractualCashFlowRun);
        legacy.Should().NotContain("attestationContext");
        JsonSerializer.Deserialize(legacy, ContractualCashFlowJsonContext.Default.ContractualCashFlowRun)!.AttestationContext.Should().BeNull();
    }

    private static ContractualCashFlowDraftAttestationSubjectResolver Resolver(ContractualCashFlowRun run, SecurityDetailDto? security)
    {
        var store = Substitute.For<IContractualCashFlowRunStore>();
        store.GetAsync(ContractualCashFlowSubjectKinds.Security, run.SubjectId, run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(run.SubjectId, run.Request.KnowledgeCutoff, Arg.Any<CancellationToken>()).Returns(security);
        return new(store, securities);
    }

    private static (ContractualCashFlowRun Run, SecurityDetailDto Security) CreateRun()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var common = JsonSerializer.SerializeToElement(new { });
        var terms = JsonSerializer.SerializeToElement(new { });
        var security = new SecurityDetailDto(id, "Bond", SecurityStatusDto.Active, "Synthetic bond", "USD", common, terms, [], [], 1, now, null);
        var request = new ContractualCashFlowRequest(new(2026, 1, 1), now);
        var contract = new ContractualCashFlowContract(new(2026, 1, 1), new(2027, 1, 1), "USD",
            [new("fixed", 1000m, 3, DayCountConvention.Actual365, .05m)]);
        var result = ContractualCashFlowEngine.Calculate(contract, request, sourceVersion: $"security:{id}:version:1",
            sourceTermsHash: ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, terms)));
        return (new ContractualCashFlowRun(Guid.NewGuid(), "Security", id, now, contract, request, result, Human.Actor)
        { AttestationContext = new(Scope.TenantId, Scope.CompanyId, Human.Actor, OperationsActionOriginDto.HumanOperator) }, security);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
