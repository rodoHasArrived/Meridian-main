using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterServiceSnapshotTests
{
    [Theory]
    [InlineData("identifier")]
    [InlineData("name")]
    [InlineData("coupon")]
    public async Task UndeclaredCdScalars_CannotActivateThroughAnUnrelatedAmendment(string edit)
    {
        var security = SecurityRecurringAccrualPolicyTests.Security("assetClassTermsVersion", "5");
        var harness = new ServiceHarness();
        harness.Seed(security.SecurityId, security.AssetClass, security.AssetSpecificTerms.GetRawText());
        var request = AmendRequest(security.SecurityId, security.AssetSpecificTerms);
        if (edit == "identifier")
            request = request with { AssetSpecificTermsPatch = null };
        else if (edit == "name")
            request = request with
            {
                AssetSpecificTermsPatch = null,
                CommonTerms = JsonSerializer.SerializeToElement(new { displayName = "Renamed synthetic CD", currency = "USD" })
            };
        else
        {
            var terms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!;
            terms["couponRate"] = 4m;
            request = request with { AssetSpecificTermsPatch = JsonSerializer.SerializeToElement(terms) };
        }

        await FluentActions.Invoking(() => harness.Service.AmendTermsAsync(request))
            .Should().ThrowExactlyAsync<SecurityMasterWriteRefusedException>()
            .WithMessage("*undeclared scalar*complete reviewed terms stamped at version 6*");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndeclaredCdScalars_RequireACompleteReviewedReplacementOrExplicitClear(bool clear)
    {
        var security = SecurityRecurringAccrualPolicyTests.Security("assetClassTermsVersion", "5");
        var harness = new ServiceHarness();
        harness.Seed(security.SecurityId, security.AssetClass, security.AssetSpecificTerms.GetRawText());
        var terms = JsonNode.Parse(security.AssetSpecificTerms.GetRawText())!.AsObject();
        if (clear)
        {
            terms.Remove("issueDate");
            terms.Remove("principal");
            terms.Remove("paymentFrequency");
        }
        else
            terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 6;

        await harness.Service.AmendTermsAsync(AmendRequest(security.SecurityId, JsonSerializer.SerializeToElement(terms)));

        var written = harness.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms;
        written.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(clear ? 5 : 6);
        written.TryGetProperty("principal", out _).Should().Be(!clear);
        written.TryGetProperty("issueDate", out _).Should().Be(!clear);
        written.TryGetProperty("paymentFrequency", out _).Should().Be(!clear);
        harness.Appended.Should().ContainSingle();
    }

    [Fact]
    public void ReviewingOneScalarWithoutTheExpandedStamp_DoesNotActivateOtherInertScalars()
    {
        var retained = SecurityRecurringAccrualPolicyTests.Security("assetClassTermsVersion", "5").AssetSpecificTerms;
        var replacement = JsonNode.Parse(retained.GetRawText())!.AsObject();
        replacement.Remove("issueDate");
        replacement["principal"] = 200_000m;
        // principal and paymentFrequency remain populated above the old stamp, even when another
        // addition is cleared. Reviewing the expanded document is explicit, never inferred.
        var amend = () => ScheduleTermsWriteRules.EnsureAmendmentDoesNotPromoteStrayContainers(
            "CertificateOfDeposit", retained, JsonSerializer.SerializeToElement(replacement));

        amend.Should().Throw<InvalidOperationException>().WithMessage("*undeclared scalar 'principal'*");
    }
}
