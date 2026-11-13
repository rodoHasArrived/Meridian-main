using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;
using System.Text.Json.Nodes;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationTemplateCatalogTests
{
    [Fact]
    public void ListEntries_ReturnsFirstTemplatePack()
    {
        var catalog = new ProviderIntegrationTemplateCatalog();

        var entries = catalog.ListEntries();

        entries.Select(entry => entry.ManifestId).Should().BeEquivalentTo(
            [
                "template-manual-csv-upload-v1",
                "template-custodian-positions-v1",
                "template-brokerage-transactions-v1",
                "template-fixed-income-security-master-v1",
                "template-fixed-income-security-master-v2"
            ]);
        entries.Single(entry => entry.ManifestId == "template-manual-csv-upload-v1")
            .Capabilities.Should().Contain(ProviderCapabilityKindDto.Positions);
        entries.Single(entry => entry.ManifestId == "template-custodian-positions-v1")
            .RequiresCredentials.Should().BeTrue();
        entries.Single(entry => entry.ManifestId == "template-fixed-income-security-master-v1")
            .IntegrationType.Should().Be(IntegrationTypeDto.SftpFile);
    }

    [Fact]
    public void FixedIncomeV2RetainsV1AndRequiresExplicitCurrencyAndBondConventions()
    {
        var catalog = new ProviderIntegrationTemplateCatalog();
        var legacy = catalog.GetManifest("template-fixed-income-security-master-v1", 1)!;
        var current = catalog.GetManifest("template-fixed-income-security-master-v2")!;
        legacy.ManifestVersion.Should().Be(1);
        legacy.FieldMappings.Single(m => m.TargetField == "security.currency").DefaultValue.Should().Be("USD");
        current.ManifestVersion.Should().Be(3);
        current.FieldMappings.Where(m => m.TargetField.EndsWith("currency", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(m => m.DefaultValue == null);
        current.FieldMappings.Select(m => m.TargetField).Should().Contain([
            "security.scheduleConventions", "security.bondWorkflow", "security.principalSchedule",
            "security.callSchedule", "bond.originalFace", "bond.currentFace", "bond.principalFactor",
            "bond.inflationRatio", "bond.quoteBasis", "bond.quoteIsDirty"]);
        current.FieldMappings.Should().NotContain(mapping => mapping.TargetField == "security.importTermsProfile");
        current.FieldMappings.GroupBy(mapping => (mapping.Capability, mapping.TargetField))
            .Should().OnlyContain(group => group.Count() == 1, "the independent workflow profile cannot inherit overlapping reviewed-profile mappings");
        catalog.GetManifest(legacy.ManifestId)!.ManifestVersion.Should().Be(2);
        catalog.GetManifest(legacy.ManifestId, 2).Should().BeSameAs(catalog.GetManifest(legacy.ManifestId));
        catalog.GetManifest(legacy.ManifestId, 3).Should().BeNull();
        var previousWorkflow = catalog.GetManifest(current.ManifestId, 2)!;
        previousWorkflow.ManifestVersion.Should().Be(2);
        previousWorkflow.FieldMappings.Should().NotContain(mapping => mapping.TargetField == "security.couponType");
        current.FieldMappings.Where(mapping => !previousWorkflow.FieldMappings.Any(previous =>
                previous.Capability == mapping.Capability && previous.TargetField == mapping.TargetField))
            .Should().OnlyContain(mapping => !mapping.Required && mapping.DefaultValue == null && mapping.ConstantValue == null);
        current.Capabilities.Should().BeEquivalentTo(previousWorkflow.Capabilities);
    }

    [Fact]
    public void FixedIncomeTemplate_RequiresExplicitReviewedConventionsAndNoCurrencyDefault()
    {
        var manifest = new ProviderIntegrationTemplateCatalog().GetManifest("template-fixed-income-security-master-v1")!;
        manifest.ManifestVersion.Should().Be(2);
        manifest.FieldMappings.Single(mapping => mapping.TargetField == "security.currency")
            .DefaultValue.Should().BeNull();
        manifest.Capabilities.Single().RequiredCanonicalFields.Should().Contain([
            "security.couponRateUnit", "security.couponCashBasis", "security.paymentFrequency", "security.dayCount",
            "security.paymentCalendars", "security.settlementConvention", "security.settlementCalendars",
            "security.callSchedule", "security.factorHistory", "security.currencyReview"]);
    }

    [Fact]
    public async Task FixedIncomeVersionPinnedRunKeepsItsEvidenceWhileFreshRunsUseTheCurrentDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-template-versions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new ProviderIntegrationTemplateCatalog();
            var legacy = catalog.GetManifest("template-fixed-income-security-master-v1", 1)!;
            var current = catalog.GetManifest(legacy.ManifestId)!;
            var store = new FileProviderIntegrationManifestStore(path);
            var at = DateTimeOffset.UtcNow;
            await store.SaveManifestAsync(legacy);
            await store.SaveConnectionAsync(new("versioned-connection", legacy.ProviderId, legacy.ManifestId,
                "Retained source", "template", default, "no-live-credentials", [ProviderCapabilityKindDto.SecurityReferenceData],
                "reviewer", at, at, null));
            var request = new ManualCsvProviderIntegrationDryRunRequestDto("retained-v1-run", legacy.ManifestId, "versioned-connection",
                ProviderCapabilityKindDto.SecurityReferenceData, "retained.csv",
                "cusip,description,maturity_date\n9128285M8,Retained source bond,2031-06-16\n", "reviewer", at);
            var service = new ProviderIntegrationDryRunService(store);
            var original = await service.RunManualCsvDryRunAsync(request);
            original.RecordsAccepted.Should().Be(1);
            await store.SaveManifestVersionAsync(current);
            (await store.CompareExchangeCurrentManifestAsync(legacy.ManifestId,
                ProviderIntegrationManifestIdentity.Create(legacy), ProviderIntegrationManifestIdentity.Create(current))).Should().BeTrue();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunManualCsvDryRunAsync(request));
            var fresh = await service.RunManualCsvDryRunAsync(request with { SyncRunId = "current-v2-run" });
            fresh.RecordsAccepted.Should().Be(0, "the reviewed current profile must refuse undeclared units and currency");
            (await store.GetRawPayloadAsync(original.SyncRunId, original.RawPayloadId))!.ManifestReference!.ManifestVersion.Should().Be(1);
            (await store.GetRawPayloadAsync(fresh.SyncRunId, fresh.RawPayloadId))!.ManifestReference!.ManifestVersion.Should().Be(2);
            (await store.ListStagingRecordsAsync(original.SyncRunId)).Should().ContainSingle();
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void RetainedBondEconomicsAndReviewedQuotationEvidenceUseTheirOwnExplicitUnitContracts()
    {
        var retained = JsonNode.Parse("""
            {"assetClass":"Bond","quantity":100,"price":{"amount":99,"currency":"USD"},
             "marketValue":{"amount":86.12,"currency":"USD"},
             "bond":{"originalFace":100,"currentFace":80,"principalFactor":0.8,"inflationRatio":1.1,
             "quoteBasis":"PercentOfCurrentFace","invoiceBasis":"IndexedPrincipal","quote":99,
             "quoteIsDirty":false,"accruedInterest":-1}}
            """)!.AsObject();
        var issues = new List<ValidationIssueDto>();
        ProviderIntegrationMappedRecordValidation.AddValidationIssues(ProviderCapabilityKindDto.Holdings, retained, issues);
        issues.Should().BeEmpty("principal factor and inflation apply once, and purchased accrued remains signed");

        var quotation = JsonNode.Parse("""
            {"assetClass":"Bond","quantity":1,"price":{"amount":98.5,"currency":"USD"},
             "marketValue":{"amount":78920,"currency":"USD"},"bond":{"originalFace":100000,
             "currentFactor":0.8,"accruedInterest":{"amount":120,"currency":"USD"},
             "quotationConvention":"Per100","priceBasis":"Clean","marketValueBasis":"Dirty"}}
            """)!.AsObject();
        ProviderIntegrationMappedRecordValidation.AddValidationIssues(ProviderCapabilityKindDto.Positions, quotation, issues);
        issues.Should().BeEmpty();
        quotation["bondValueReconciliation"]!["status"]!.GetValue<string>().Should().Be("Matched");
        decimal.Parse(quotation["bondValueReconciliation"]!["dirtyConsideration"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture).Should().Be(78920m);

        retained["bond"]!["currentFactor"] = 0.7m;
        ProviderIntegrationMappedRecordValidation.AddValidationIssues(ProviderCapabilityKindDto.Holdings, retained, issues);
        issues.Should().Contain(issue => issue.Code == "bond.normalization.blocked"
            && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
    }

    [Fact]
    public void ListManifests_SeedsRequiredMappingsForEveryEnabledCapability()
    {
        var catalog = new ProviderIntegrationTemplateCatalog();

        foreach (var manifest in catalog.ListManifests())
        {
            var readiness = ProviderIntegrationActivationReadinessService.Evaluate(manifest);

            readiness.Issues.Select(issue => issue.Code)
                .Should()
                .NotContain("provider-manifest.required-mapping-missing")
                .And.NotContain("provider-manifest.mapping-review-required")
                .And.NotContain("provider-manifest.certified-adapter-required");
            readiness.RequiredEvidence.Should().Contain("dry-run-result");
            foreach (var capability in manifest.Capabilities.Where(capability => capability.Enabled))
            {
                capability.RequiresCertifiedAdapter.Should().BeFalse();
                capability.RequiredCanonicalFields.Should().NotBeEmpty();
                manifest.FieldMappings
                    .Where(mapping => mapping.Capability == capability.Capability)
                    .Select(mapping => mapping.TargetField)
                    .Should()
                    .Contain(capability.RequiredCanonicalFields);
            }
        }
    }
}
