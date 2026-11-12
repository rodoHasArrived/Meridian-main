using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Tests.Ui;

public sealed class SinglePrincipalAttestationAdditionalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-attestation-snapshot-{Guid.NewGuid():N}");
    private static readonly SinglePrincipalAttestationScopeDto Scope = new("tenant-one", "company-one");
    private static readonly SinglePrincipalAttestationAuthority Principal = new(
        "preparer-one", OperationsActionOriginDto.HumanOperator, CanRead: true,
        CanAdministerDeclaration: true, CanAttest: true, CanModifyTarget: true);
    private const string OriginalOperands = "{\"version\":1,\"document\":{\"amount\":100,\"reviewState\":\"Unreviewed\"}}";

    private static SinglePrincipalAttestationSubjectSnapshotDto Snapshot(string json = OriginalOperands) => new(
        "evidence-document", "vault-one/document-one", Sha256Digest.ComputeUtf8(json), Sha256Digest.ComputeUtf8(json),
        "source-one", Sha256Digest.ComputeUtf8("original source bytes"), "external-provider", "Unclassified", true,
        ["Retained source bytes verified", "Complete extraction checks"], "account", "account-one", json);

    private SinglePrincipalAttestationStore Store(SinglePrincipalAttestationSubjectSnapshotDto? snapshot = null, bool enabled = true) =>
        new(_root, (_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(snapshot ?? Snapshot()), enabled);

    private static ChangeSinglePrincipalDeclarationRequestDto Declaration(long revision = 0, bool enabled = true) => new(
        Guid.NewGuid(), revision, enabled, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "One principal controls this scope");

    private static RecordPreparerAttestationRequestDto Acknowledge(SinglePrincipalAttestationSubjectSnapshotDto snapshot) => new(
        Guid.NewGuid(), 1, SinglePrincipalAttestationPolicy.EvidenceAcknowledgment, snapshot.SubjectKind,
        snapshot.SubjectId, snapshot.Revision, snapshot.Hash, "Inspected the retained document and checks",
        SinglePrincipalAttestationPolicy.Confirmation);

    [Fact]
    public async Task ReceiptRetainsOriginalEvaluatedOperandsAfterSourceMetadataChanges()
    {
        var original = Snapshot();
        await Store(original).ChangeDeclarationAsync(Scope, Principal, Declaration());
        var receipt = await Store(original).AttestAsync(Scope, Principal, Acknowledge(original));
        var later = Snapshot("{\"version\":1,\"document\":{\"amount\":200,\"reviewState\":\"Accepted\"}}");

        var historical = (await Store(later).GetAsync(Scope, Principal)).Receipts.Single();

        historical.Should().BeEquivalentTo(receipt);
        historical.SubjectSnapshotJson.Should().Be(OriginalOperands);
        Sha256Digest.ComputeUtf8(historical.SubjectSnapshotJson!).Should().Be(historical.SubjectHash);
        historical.SubjectHash.Should().NotBe(later.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"version\":1,\"document\":{\"amount\":999}}")]
    public async Task MissingOrTamperedSnapshotCannotProduceAReceipt(string? changedJson)
    {
        var original = Snapshot();
        await Store(original).ChangeDeclarationAsync(Scope, Principal, Declaration());
        var invalid = original with { SubjectSnapshotJson = changedJson };

        var error = await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() =>
            Store(invalid).AttestAsync(Scope, Principal, Acknowledge(original)));

        error.Code.Should().Be("unsupported_subject");
        var state = await Store().GetAsync(Scope, Principal);
        state.Receipts.Should().BeEmpty();
        state.AuditTrail.Should().ContainSingle().Which.Action.Should().Be("DeclarationEnabled");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"not an evaluated object\"")]
    public async Task MatchingDigestDoesNotMakeMalformedOrNonObjectOperandsValid(string json)
    {
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        var invalid = Snapshot(json);

        var error = await Assert.ThrowsAsync<SinglePrincipalAttestationException>(() =>
            Store(invalid).AttestAsync(Scope, Principal, Acknowledge(invalid)));

        error.Code.Should().Be("unsupported_subject");
        (await Store().GetAsync(Scope, Principal)).Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task DisablingHostPolicyStillAllowsWithdrawalAndCorrectiveSelfRevocation()
    {
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        var receipt = await Store().AttestAsync(Scope, Principal, Acknowledge(Snapshot()));
        var disabled = Store(enabled: false);

        await disabled.ChangeDeclarationAsync(Scope, Principal, Declaration(1, enabled: false));
        await disabled.RevokeAsync(Scope, Principal,
            new(Guid.NewGuid(), receipt.ReceiptId, 1, "Correcting an erroneous earlier assertion"));

        var state = await disabled.GetAsync(Scope, Principal);
        state.PolicyEnabled.Should().BeFalse();
        state.Declaration!.Enabled.Should().BeFalse();
        state.Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(receipt);
        state.Revocations.Should().ContainSingle();
    }

    [Fact]
    public async Task UncertainResponseAfterAtomicCommitReplaysOneHistoricalReceipt()
    {
        await Store().ChangeDeclarationAsync(Scope, Principal, Declaration());
        var snapshot = Snapshot();
        var command = Acknowledge(snapshot);
        var uncertain = new SinglePrincipalAttestationStore(_root,
            (_, _, _, _) => Task.FromResult<SinglePrincipalAttestationSubjectSnapshotDto?>(snapshot), true,
            async (path, content, ct) =>
            {
                await AtomicFileWriter.WriteAsync(path, content, ct);
                throw new IOException("Response interrupted after the atomic commit");
            });
        await Assert.ThrowsAsync<IOException>(() => uncertain.AttestAsync(Scope, Principal, command));

        var recovered = await Store(enabled: false).AttestAsync(Scope,
            Principal with { CanAttest = false, CanModifyTarget = false }, command);

        var state = await Store().GetAsync(Scope, Principal);
        state.Receipts.Should().ContainSingle().Which.Should().BeEquivalentTo(recovered);
        state.AuditTrail.Should().HaveCount(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
