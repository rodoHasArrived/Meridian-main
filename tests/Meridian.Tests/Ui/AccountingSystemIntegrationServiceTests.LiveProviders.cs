using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Tests.DataIntegration.AccountingSystem;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task LiveProviders_RequireOwnedControls_AndInvalidateCertificationAfterConnectionChange(string id)
    {
        var credentials = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, credentials, client);
        var ledger = CreateMatchedFixtureLedgerStore([("100", LedgerAccountType.Asset, 100.25m, 0m), ("300", LedgerAccountType.Equity, 0m, 100.25m)]);
        var service = CreateService(ledger, provider);
        var import = await service.ImportAsync(ExternalGlTestData.Request(id) with { FundProfileId = "default-fund", TenantId = null, CompanyId = null });
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-certified",
            AccountMappings = new Dictionary<string, string> { ["100"] = "cash", ["300"] = "capital" },
            DimensionMappings = template.DimensionMappings.Select(m => m with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var request = new AccountingSystemExportPackageRequestDto("accounting-ops", id, "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31), profile.ProfileId, RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, id)]);
        var blocked = await service.CreateExportPackageAsync(request);
        blocked.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        blocked.ValidationIssues.Should().Contain(i => i.Code.StartsWith("ExternalGlProviderControlMissing:"));
        var controls = id == "xero"
            ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(c => $"approval:external-gl-provider:{id}:{c}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31").ToArray();
        var package = await service.CreateExportPackageAsync(request with { EvidenceLinks = [.. request.EvidenceLinks, .. evidence] });
        package.ValidationIssues.Should().NotContain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        var certificationRequest = new CertifyAccountingSystemExportPackageRequestDto(package.ExportPackageId, "controller",
            "Reviewed provider controls and retained account scope.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31"]);
        await service.Invoking(s => s.CertifyExportPackageAsync(certificationRequest with { ActionOrigin = OperationsActionOriginDto.AssistantDraft }))
            .Should().ThrowAsync<InvalidOperationException>();
        var certified = await service.CertifyExportPackageAsync(certificationRequest);
        certified!.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certified.PostingEnabled.Should().BeFalse();
        (await service.GetExportPackageManifestAsync(package.ExportPackageId))!.ExternalPostingAllowed.Should().BeFalse();
        credentials.Values[id == "xero" ? "TenantId" : "SubsidiaryId"] = id == "xero" ? Guid.NewGuid().ToString() : "99";
        var stale = await service.GetExportPackageManifestAsync(package.ExportPackageId);
        stale!.ValidationIssues.Should().Contain(i => i.Code == "ExternalGlProviderImportScopeMismatch");
        stale.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Blocked);
        stale.ExternalPostingAllowed.Should().BeFalse();
    }
}
