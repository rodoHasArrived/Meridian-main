using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.UnitTests.SecurityMaster;

public sealed class SecurityVersionedScalarTermsTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void IntroducedScalars_ReadOnlyAtTheirOwnDocumentVersion(int version, bool expected)
    {
        var terms = JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = version,
            issueDate = "2026-01-01",
            principal = 25000m,
            paymentFrequency = "AtMaturity"
        });

        foreach (var key in new[] { "issueDate", "principal", "paymentFrequency" })
            Assert.Equal(expected, SecurityVersionedScalarTerms.CanDecode("CertificateOfDeposit", terms, key, writeMode: false));
    }

    [Fact]
    public void ExplicitWrites_UseTheWritableCeilingInsteadOfAnOldDocumentStamp()
    {
        var terms = JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 1, principal = 25000m });

        Assert.True(SecurityVersionedScalarTerms.CanDecode("CertificateOfDeposit", terms, "principal", writeMode: true));
        Assert.False(SecurityVersionedScalarTerms.CanDecode("CertificateOfDeposit", terms, "unknown", writeMode: true));
    }

    [Fact]
    public void AbsentAdditions_DoNotChangeTheLegacyWrittenShapeOrDecimalScale()
    {
        const string payload = "{\"couponRate\":5.2500,\"issueDate\":null,\"issuerName\":\"Synthetic bank\",\"principal\":null,\"paymentFrequency\":null}";

        var written = SecurityVersionedScalarTerms.OmitAbsent(payload, ["issueDate", "principal", "paymentFrequency"]);

        Assert.Equal("{\"couponRate\":5.2500,\"issuerName\":\"Synthetic bank\"}", written);
    }

    [Fact]
    public void PresentZeroAndOtherLegacyNullFields_ArePreserved()
    {
        const string payload = "{\"couponRate\":null,\"principal\":0,\"issueDate\":\"2026-01-01\"}";

        Assert.Equal(payload, SecurityVersionedScalarTerms.OmitAbsent(payload, ["issueDate", "principal", "paymentFrequency"]));
    }
}
