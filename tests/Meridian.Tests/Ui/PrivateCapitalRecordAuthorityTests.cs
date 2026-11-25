using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
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
    private static readonly Guid HolderId = Guid.Parse("a8111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Choices_ExcludeGuaranteesAndOtherClassesFromPrivateCapitalVehicles()
    {
        var fund = Security("PrivateFundInterest");
        var authority = Authority(fund, Security("CommitmentGuarantee"), Security("PrivateCompanyEquity"),
            Security("Equity"), Security("Unknown"), Security("NotAClass"));

        var choices = await authority.ChoicesAsync(new("tenant-a", "company-a"), CancellationToken.None);

        Assert.Equal(fund.SecurityId, Assert.Single(choices.Vehicles).Id);
        Assert.Equal(HolderId, Assert.Single(choices.Entities).Id);
    }

    [Theory]
    [InlineData("PrivateFundInterest", SecurityStatusDto.Active, "USD", true)]
    [InlineData("CommitmentGuarantee", SecurityStatusDto.Active, "USD", false)]
    [InlineData("PrivateCompanyEquity", SecurityStatusDto.Active, "USD", false)]
    [InlineData("Equity", SecurityStatusDto.Active, "USD", false)]
    [InlineData("Unknown", SecurityStatusDto.Active, "USD", false)]
    [InlineData("NotAClass", SecurityStatusDto.Active, "USD", false)]
    [InlineData("PrivateFundInterest", SecurityStatusDto.Inactive, "USD", false)]
    [InlineData("PrivateFundInterest", SecurityStatusDto.Unknown, "USD", false)]
    [InlineData("PrivateFundInterest", SecurityStatusDto.Active, "EUR", false)]
    public async Task Register_RequiresExplicitClassCapabilityActiveStatusAndVehicleCurrency(
        string assetClass, SecurityStatusDto status, string commitmentCurrency, bool accepted)
    {
        var security = Security(assetClass) with { Status = status };
        var authority = Authority(security);
        var scope = new PrivateCapitalScope("tenant-a", "company-a");

        if (!accepted)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => authority.ValidateRegisterAsync(
                scope, HolderId, security.SecurityId, commitmentCurrency, CancellationToken.None));
            return;
        }

        var vehicle = await authority.ValidateRegisterAsync(
            scope, HolderId, security.SecurityId, commitmentCurrency, CancellationToken.None);

        Assert.Equal(security.DisplayName, vehicle.Name);
        Assert.Equal("Private equity", vehicle.Strategy);
        Assert.Equal(2021, vehicle.VintageYear);
        Assert.Equal(security.Version, vehicle.Version);
    }

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
                TenantId = "tenant-a",
                Scope = "company-a",
                Actor = "operator",
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

    private static PrivateCapitalRecordAuthority Authority(params SecurityDetailDto[] records)
    {
        var structures = Substitute.For<IFundStructureService>();
        var graph = new OrganizationStructureGraphDto([], [], [], [], [], [],
            [new(HolderId, LegalEntityTypeDto.LimitedPartner, "holder", "Synthetic holder", "US", "USD",
                true, DateTimeOffset.UnixEpoch, null)], [], [], [], [], []);
        structures.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(graph));
        var securities = Substitute.For<ISecurityMasterQueryService>();
        IReadOnlyList<SecuritySummaryDto> summaries = records.Select(record => new SecuritySummaryDto(
            record.SecurityId, record.AssetClass, record.Status, record.DisplayName, "synthetic", record.Currency, record.Version)).ToArray();
        securities.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summaries));
        foreach (var record in records)
            securities.GetByIdAsync(record.SecurityId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<SecurityDetailDto?>(record));
        return new(structures, securities, Substitute.For<IEvidenceArtifactStore>());
    }

    private static SecurityDetailDto Security(string assetClass)
        => new(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, "Synthetic vehicle", "USD", default,
            JsonSerializer.SerializeToElement(new { strategy = "Private equity", vintage = 2021 }),
            [], [], 7, DateTimeOffset.UnixEpoch, null);
}
