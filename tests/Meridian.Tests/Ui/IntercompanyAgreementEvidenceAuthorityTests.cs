using System.Text;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
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
    public async Task RetainedReviewedAndLinkedPdfAboveOneMiB_ResolvesExactNativeLegalEvidence()
    {
        var id = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\nSynthetic signed agreement\n" + new string('x', 1024 * 1024));
        var retained = await RetainAsync(id, bytes: bytes);
        var resolved = Assert.Single(await Authority().ResolveAsync(new(Tenant, "owner"), id, Guid.NewGuid(), [retained]));
        Assert.Equal(retained.Reference, resolved.Reference);
        Assert.Equal(retained.ArtifactId, resolved.ArtifactId);
        Assert.Equal(retained.ContentHash, resolved.ContentHash);
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
            "document" => document with { Reference = EvidenceVaultReference.CanonicalPrefix + "wrong-document" },
            "hash" => document with { ContentHash = new string('b', 64) },
            _ => document
        };
        var authority = Authority(company: mismatch == "scope" ? "another-company" : Company);
        if (mismatch == "tenant")
            await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => authority.ResolveAsync(
                new("another-tenant", "owner"), agreementId, Guid.NewGuid(), [document]));
        else
            await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() => authority.ResolveAsync(
                new(Tenant, "owner"), agreementId, Guid.NewGuid(), [document]));
    }

    [Fact]
    public async Task AcceptedManifestWithoutOriginalBytes_DoesNotAuthorizeAgreement()
    {
        var agreementId = Guid.NewGuid();
        var document = await RetainAsync(agreementId);
        var retainedFile = Assert.Single(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(retainedFile, "different retained bytes");
        await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() =>
            Authority().ResolveAsync(new(Tenant, "owner"), agreementId, Guid.NewGuid(), [document]));
    }

    private async Task<IntercompanyAgreementEvidenceDto> RetainAsync(Guid agreementId, bool accept = true,
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
        return new(EvidenceVaultReference.CanonicalPrefix + intake.VaultIdentity.VaultId, document.ArtifactId!, document.SourceHashSha256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private WorkstationIntercompanyAgreementEvidenceAuthority Authority(string company = Company)
    {
        var http = new DefaultHttpContext();
        http.Items[LoginSessionMiddleware.CurrentUserKey] = "owner";
        http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
        http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = company;
        return new(new HttpContextAccessor { HttpContext = http }, Store());
    }
}
