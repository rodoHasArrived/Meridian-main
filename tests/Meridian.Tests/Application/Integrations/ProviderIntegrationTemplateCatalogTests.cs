using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

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
                "template-fixed-income-security-master-v1"
            ]);
        entries.Single(entry => entry.ManifestId == "template-manual-csv-upload-v1")
            .Capabilities.Should().Contain(ProviderCapabilityKindDto.Positions);
        entries.Single(entry => entry.ManifestId == "template-custodian-positions-v1")
            .RequiresCredentials.Should().BeTrue();
        entries.Single(entry => entry.ManifestId == "template-fixed-income-security-master-v1")
            .IntegrationType.Should().Be(IntegrationTypeDto.SftpFile);
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
