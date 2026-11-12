using System.Text;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementEvidenceAuthorityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "signed-agreement", Guid.NewGuid().ToString("N"));
    private const string Tenant = "agreement-tenant";
    private const string Company = "agreement-company";
    private FileEvidenceArtifactStore Store() => new(_root, NullLogger<FileEvidenceArtifactStore>.Instance);

    [Fact]
    public async Task DraftFirst_RetainedReviewedAndLinkedPdfAboveOneMiB_CanApproveExactSuccessorRevision()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var id = Guid.NewGuid();
        var evidence = new EvidenceVaultIntercompanyAgreementEvidenceAuthority(Store());
        var service = new IntercompanyAgreementService(new InMemoryIntercompanyAgreementStore(), new Scope(), evidence);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var draft = await service.CreateAsync(Tenant, "owner", new(Guid.NewGuid(), id, Guid.NewGuid(), "Signed facility",
            IntercompanyAgreementKindDto.Loan, first, second, "Lender", "Borrower", "USD", "Monthly", start, null,
            [new(first, Guid.NewGuid(), "ar", "ap"), new(second, Guid.NewGuid(), "ar", "ap")]));
        Assert.Null(draft.SignedDocument);

        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\nSynthetic signed agreement\n" + new string('x', 1024 * 1024));
        var signed = await RetainAsync(id, bytes: bytes);
        var attached = await service.ReviseAsync(Tenant, "owner", id, new(Guid.NewGuid(), draft.Version,
            draft.FacilityReference, draft.PartyOneRole, draft.PartyTwoRole, draft.Currency, draft.SettlementCadence,
            draft.EffectiveFrom, null, draft.PartyMappings, signed, "Attach reviewed signed evidence to saved draft"));
        var submitted = await service.ApplyActionAsync(Tenant, "owner", id, IntercompanyAgreementActionDto.Submit,
            new(Guid.NewGuid(), attached.Version));
        var approved = await service.ApplyActionAsync(Tenant, "owner", id, IntercompanyAgreementActionDto.Approve,
            new(Guid.NewGuid(), submitted.Version));
        Assert.Equal(IntercompanyAgreementStatusDto.Approved, approved.Status);
        Assert.Equal(attached.RevisionId, approved.RevisionId);
        Assert.NotEqual(draft.RevisionId, approved.RevisionId);
        Assert.Equal(signed, approved.SignedDocument);
        Assert.Equal("owner", approved.ApprovedBy);
    }

    [Theory]
    [InlineData("unreviewed")]
    [InlineData("classification")]
    [InlineData("link-kind")]
    [InlineData("link-id")]
    [InlineData("artifact")]
    [InlineData("document")]
    [InlineData("hash")]
    [InlineData("tenant")]
    [InlineData("scope")]
    public async Task ApprovalEvidence_RefusesWrongReviewClassificationLinkOrPinnedIdentity(string mismatch)
    {
        var agreementId = Guid.NewGuid();
        var document = await RetainAsync(agreementId,
            accept: mismatch != "unreviewed",
            classification: mismatch == "classification" ? EvidenceDocumentClassificationDto.BankEvidence : EvidenceDocumentClassificationDto.Agreement,
            linkKind: mismatch == "link-kind" ? EvidenceDocumentLinkKindDto.Journal : EvidenceDocumentLinkKindDto.IntercompanyAgreement,
            linkedAgreement: mismatch == "link-id" ? Guid.NewGuid() : agreementId);
        document = mismatch switch
        {
            "artifact" => document with { ArtifactId = "wrong-artifact" },
            "document" => document with { DocumentId = "wrong-document" },
            "hash" => document with { ContentHashSha256 = new string('b', 64) },
            "scope" => document with { Scope = "another-company" },
            _ => document
        };
        var authority = new EvidenceVaultIntercompanyAgreementEvidenceAuthority(Store());
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.VerifyAsync(
            mismatch == "tenant" ? "another-tenant" : Tenant, agreementId, document));
    }

    [Fact]
    public async Task AcceptedManifestWithoutOriginalBytes_DoesNotAuthorizeAgreement()
    {
        var agreementId = Guid.NewGuid();
        var document = await RetainAsync(agreementId);
        var retainedFile = Assert.Single(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(retainedFile, "different retained bytes");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EvidenceVaultIntercompanyAgreementEvidenceAuthority(Store()).VerifyAsync(Tenant, agreementId, document));
    }

    private async Task<IntercompanySignedDocumentDto> RetainAsync(Guid agreementId, bool accept = true,
        EvidenceDocumentClassificationDto classification = EvidenceDocumentClassificationDto.Agreement,
        EvidenceDocumentLinkKindDto linkKind = EvidenceDocumentLinkKindDto.IntercompanyAgreement,
        Guid? linkedAgreement = null, byte[]? bytes = null)
    {
        bytes ??= Encoding.UTF8.GetBytes("%PDF-1.7\nSynthetic signed agreement\n");
        var store = Store();
        var intake = await store.WriteIntakeArtifactAsync(new("accounting-record", "current", "upload",
            "signed-agreement.pdf", Convert.ToBase64String(bytes), "application/pdf", ReceivedBy: "owner")
        {
            Actor = "owner", TenantId = Tenant, Scope = Company, Classification = classification,
            ObjectLinks = [new(linkKind, (linkedAgreement ?? agreementId).ToString("D"), Relationship: "supports")]
        });
        var document = intake.Document!;
        if (accept)
        {
            var reviewed = await store.ReviewDocumentAsync(intake.VaultIdentity.VaultId, document.DocumentId, Tenant, Company,
                new(EvidenceDocumentReviewStatusDto.Accepted, "owner", "Reviewed signed agreement against retained bytes")
                {
                    ConfirmedFields = [new("sourceHashSha256", document.SourceHashSha256, "owner", DateTimeOffset.UtcNow)]
                });
            Assert.NotNull(reviewed);
            document = reviewed.Entry.Document;
        }
        return new(intake.VaultIdentity.VaultId, document.DocumentId, document.ArtifactId!, document.SourceHashSha256, Company);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Scope : IIntercompanyAgreementScopeAuthority
    {
        public Task<IAsyncDisposable> AcquireLeaseAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable>(new Lease());
        public Task ValidateAsync(string tenantId, IntercompanyAgreementDto agreement, bool requireActive, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>>([
                new(agreement.PartyOneEntityId, 1, new string('a', 64)), new(agreement.PartyTwoEntityId, 1, new string('b', 64))]);
    }
    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
