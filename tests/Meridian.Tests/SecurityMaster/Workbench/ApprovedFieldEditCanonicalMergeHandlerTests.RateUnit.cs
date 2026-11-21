using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed partial class ApprovedFieldEditCanonicalMergeHandlerTests
{
    [Fact]
    public async Task ApprovedUnitRepair_PromotesAndPersistsCanonicalDeclarationWithoutChangingRate()
    {
        using var source = JsonDocument.Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"issuerName":"Synthetic Bank","maturity":"2029-01-01","couponRate":0.5,"dayCount":"Actual360"}""");
        SetProjection("CertificateOfDeposit", source.RootElement.Clone());
        const string path = "assetSpecificTerms.rateUnit";
        var revisionId = await SeedApprovedFieldRevisionAsync(path, "Percent");
        var canonical = new CanonicalAmendHarness(_projectionStore, revisionStore: _revisions);

        await CreateCanonicalMergeHandler(canonical.Service)
            .HandleAsync(Event(revisionId, path) with { EffectiveFrom = DateTimeOffset.UtcNow });

        var projection = canonical.Upserted.Should().ContainSingle().Subject;
        projection.AssetSpecificTerms.GetProperty("couponRate").GetDecimal().Should().Be(0.5m);
        SecurityRecordRateUnit.Read(projection.AssetClass, projection.AssetSpecificTerms).Should().Be(RateUnit.Percent);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(projection.AssetSpecificTerms).Should().Be(5);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        economic.EconomicTerms.GetProperty("rateConvention").GetProperty("rateUnit").GetString().Should().Be("Percent");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UngovernedUnitAmendment_IsRefusedEvenWhenDirectEditsArePermitted(bool systemIngest)
    {
        using var source = JsonDocument.Parse("""{"schemaVersion":1,"issuerName":"Synthetic Bank","maturity":"2029-01-01","couponRate":0.5,"dayCount":"Actual360"}""");
        SetProjection("CertificateOfDeposit", source.RootElement.Clone());
        var canonical = new CanonicalAmendHarness(_projectionStore);
        using var patch = JsonDocument.Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":5,"issuerName":"Synthetic Bank","maturity":"2029-01-01","couponRate":0.5,"dayCount":"Actual360","rateUnit":"Percent"}""");
        var request = new AmendSecurityTermsRequest(SecurityId, 3, null, patch.RootElement.Clone(), [], [],
            DateTimeOffset.UtcNow, systemIngest ? "provider-ingest" : "desk-tool", "ops.analyst", null, "Unit repair");
        Func<Task> act = systemIngest
            ? () => ((ISecurityMasterAmender)canonical.Service).AmendTermsAsync(request)
            : () => canonical.Service.AmendTermsAsync(request);

        await act.Should().ThrowAsync<SecurityMasterWriteRefusedException>().WithMessage("*approved workbench*rate unit*");
        canonical.Upserted.Should().BeEmpty();
        canonical.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovedRevisionOfAnotherField_CannotAuthorizeAUnitCorrection()
    {
        using var source = JsonDocument.Parse("""{"schemaVersion":1,"issuerName":"Synthetic Bank","maturity":"2029-01-01","couponRate":0.5,"dayCount":"Actual360"}""");
        SetProjection("CertificateOfDeposit", source.RootElement.Clone());
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.issuerName", "Different Bank");
        var canonical = new CanonicalAmendHarness(_projectionStore, revisionStore: _revisions);
        using var patch = JsonDocument.Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":5,"issuerName":"Synthetic Bank","maturity":"2029-01-01","couponRate":0.5,"dayCount":"Actual360","rateUnit":"Percent"}""");
        var request = new AmendSecurityTermsRequest(SecurityId, 3, null, patch.RootElement.Clone(), [], [],
            DateTimeOffset.UtcNow, "operator-workbench", "ops.analyst", revisionId.ToString("D"), "Unit repair");

        await canonical.Service.Invoking(service => service.AmendApprovedRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>().WithMessage("*exact unit*");
        canonical.Upserted.Should().BeEmpty();
        canonical.Appended.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UngovernedUnitVersionDowngrade_CannotReviveADifferentFieldDefault(bool systemIngest)
    {
        using var source = JsonDocument.Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"depositType":"TimeDeposit","institutionName":"Synthetic Bank","interestRate":0.5,"rateUnit":"Percent","isCallable":false}""");
        SetProjection("Deposit", source.RootElement.Clone());
        var patch = System.Text.Json.Nodes.JsonNode.Parse(source.RootElement.GetRawText())!.AsObject();
        patch[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 1;
        var canonical = new CanonicalAmendHarness(_projectionStore);
        var request = new AmendSecurityTermsRequest(SecurityId, 3, null, JsonSerializer.SerializeToElement(patch), [], [],
            DateTimeOffset.UtcNow, systemIngest ? "provider-ingest" : "desk-tool", "ops.analyst", null, "Version downgrade");
        Func<Task> act = systemIngest
            ? () => ((ISecurityMasterAmender)canonical.Service).AmendTermsAsync(request)
            : () => canonical.Service.AmendTermsAsync(request);

        await act.Should().ThrowAsync<SecurityMasterWriteRefusedException>();
        canonical.Upserted.Should().BeEmpty();
        canonical.Appended.Should().BeEmpty();
    }

    [Theory]
    [InlineData("42", "common")]
    [InlineData("{}", "deactivate")]
    [InlineData("42", "unrelated-terms")]
    [InlineData("{}", "common")]
    public async Task MalformedRetainedUnit_CannotDisappearThroughAnUnrelatedMutation(string rawUnit, string operation)
    {
        using var source = JsonDocument.Parse($$"""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"depositType":"TimeDeposit","institutionName":"Synthetic Bank","interestRate":0.5,"rateUnit":{{rawUnit}},"isCallable":false}""");
        SetProjection("Deposit", source.RootElement.Clone());
        var canonical = new CanonicalAmendHarness(_projectionStore);
        var common = JsonSerializer.SerializeToElement(new { displayName = "Renamed synthetic deposit", currency = "USD" });
        var request = new AmendSecurityTermsRequest(SecurityId, 3, common,
            operation == "unrelated-terms" ? source.RootElement.Clone() : null, [], [],
            DateTimeOffset.UtcNow, "desk-tool", "ops.analyst", null, "Unrelated mutation");
        Func<Task> act = operation == "deactivate"
            ? () => canonical.Service.DeactivateAsync(new DeactivateSecurityRequest(SecurityId, 3, DateTimeOffset.UtcNow, "desk-tool", "ops.analyst", null, "Deactivate"))
            : () => canonical.Service.AmendTermsAsync(request);

        await act.Should().ThrowAsync<SecurityMasterWriteRefusedException>().WithMessage("*approved*rateUnit*repair*");
        canonical.Upserted.Should().BeEmpty();
        canonical.Appended.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResubmittingAnOldStrayUnit_IsAGovernedActivationEvenWhenTheRawKeyIsUnchanged(bool systemIngest)
    {
        using var source = JsonDocument.Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"depositType":"TimeDeposit","institutionName":"Synthetic Bank","interestRate":0.5,"rateUnit":"Percent","isCallable":false}""");
        SetProjection("Deposit", source.RootElement.Clone());
        var canonical = new CanonicalAmendHarness(_projectionStore);
        var request = new AmendSecurityTermsRequest(SecurityId, 3, null, source.RootElement.Clone(), [], [],
            DateTimeOffset.UtcNow, systemIngest ? "provider-ingest" : "desk-tool", "ops.analyst", null, "Resubmit retained terms");
        Func<Task> act = systemIngest
            ? () => ((ISecurityMasterAmender)canonical.Service).AmendTermsAsync(request)
            : () => canonical.Service.AmendTermsAsync(request);

        await act.Should().ThrowAsync<SecurityMasterWriteRefusedException>();
        canonical.Upserted.Should().BeEmpty();
        canonical.Appended.Should().BeEmpty();
    }

}
