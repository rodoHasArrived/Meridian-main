using System.Text;
using Meridian.Contracts.PrivateCapital;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalRecordAuthorityTests
{
    [Fact]
    public async Task ExactRetainedDocumentBytesAndScopeAreRequired_AcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "capital-evidence-" + Guid.NewGuid());
        try
        {
            var store = new FileEvidenceArtifactStore(path, NullLogger<FileEvidenceArtifactStore>.Instance);
            var intake = await store.WriteIntakeArtifactAsync(new("report-pack", "source", "upload", "gp-statement.txt",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("Capital statement: commitment 1000, called 100.")), "text/plain")
            {
                TenantId = "tenant-a", Scope = "company-a", Actor = "operator",
                Classification = EvidenceDocumentClassificationDto.Statement
            });
            var identity = intake.VaultIdentity;
            var document = Assert.Single(identity.Documents);
            var evidence = new PrivateCapitalEvidence(identity.VaultId, document.DocumentId, document.SourceHashSha256);
            var restarted = new FileEvidenceArtifactStore(path, NullLogger<FileEvidenceArtifactStore>.Instance);
            var authority = new PrivateCapitalRecordAuthority(Substitute.For<IFundStructureService>(),
                Substitute.For<ISecurityMasterQueryService>(), restarted);
            await authority.ValidateEvidenceAsync(new("tenant-a", "company-a"), evidence, CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.ValidateEvidenceAsync(new("tenant-b", "company-a"), evidence, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ValidateEvidenceAsync(new("tenant-a", "company-a"),
                evidence with { ContentHash = new('f', 64) }, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ValidateEvidenceAsync(new("tenant-a", "company-a"),
                evidence with { DocumentId = "different-document" }, CancellationToken.None));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
