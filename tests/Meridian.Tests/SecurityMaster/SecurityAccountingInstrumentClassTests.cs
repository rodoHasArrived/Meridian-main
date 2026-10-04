using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The accounting-slice classification is a DECLARED capability of the asset class, not something
/// inferred from the record's classification prose. These lock the resolution the Security Master
/// accounting adapter depends on, and the cash-sweep regression that motivated it.
/// </summary>
public sealed class SecurityAccountingInstrumentClassTests
{
    [Fact]
    public void CashSweep_IsNotClassifiedAsASecuritizedInstrument()
    {
        // Regression: CashSweep and StructuredCredit both carried AssetFamily.StructuredCash, and the
        // adapter matched that literal as "structured credit". Every cash-sweep vehicle resolved to
        // AssetBackedSecurity, passed the fixed-income gate, and — carrying no accounting rule —
        // raised a High-severity SECURITY_ACCOUNTING_RULE_MISSING break instead of the benign Info
        // that says the instrument is outside this slice.
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(
                "CashSweep", "CashSweep", "CashSweep", "CashEquivalent")
            .Should().BeNull();
    }

    [Fact]
    public void StructuredCash_NoLongerNamesASecuritizedFamily()
    {
        // The family label is a reporting rollup and must not classify anything on its own. Offering
        // it as a declared class name must resolve nothing.
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("StructuredCash").Should().BeNull();
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CorporateBond")]
    [InlineData("MunicipalBond")]
    [InlineData("TreasuryBill")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("CommercialPaper")]
    public void FixedIncomeClasses_ResolveToBond(string declaredClassName)
    {
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(declaredClassName)
            .Should().Be(SecurityAccountingInstrumentClasses.Bond);
    }

    [Theory]
    [InlineData("StructuredCredit")]
    [InlineData("MortgageBacked")]
    [InlineData("MBS")]
    [InlineData("AssetBacked")]
    [InlineData("ABS")]
    public void SecuritizedClasses_ResolveToAssetBackedSecurity(string declaredClassName)
    {
        // ADR-022 gives securitized products one canonical home, so every vendor spelling of a
        // securitized tranche posts under the same accounting class.
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(declaredClassName)
            .Should().Be(SecurityAccountingInstrumentClasses.AssetBackedSecurity);
    }

    [Fact]
    public void ASpecificClassWinsOverTheCoarseTaxonomyBucket()
    {
        // StructuredCredit is a FixedIncome record. The specific class must decide, or every
        // securitized tranche would post as a plain bond and skip factor-driven paydown.
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("StructuredCredit", "FixedIncome")
            .Should().Be(SecurityAccountingInstrumentClasses.AssetBackedSecurity);

        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("SomeUnmodelledClass", "FixedIncome")
            .Should().Be(SecurityAccountingInstrumentClasses.Bond);
    }

    [Theory]
    [InlineData("Equity")]
    [InlineData("Option")]
    [InlineData("MoneyMarketFund")]
    [InlineData("Deposit")]
    [InlineData("DirectLoan")]
    [InlineData("PrivateFundInterest")]
    public void ClassesOutsideTheSlice_ResolveToNothing(string declaredClassName)
    {
        // Null means "outside this accounting slice", which the event service reports as
        // SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT at Info severity. DirectLoan is deliberately here:
        // canonical direct loans were never admitted, and the classification now says so uniformly
        // instead of admitting only records whose class string happened to read "Loan".
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(declaredClassName)
            .Should().BeNull();
    }

    [Fact]
    public void BlankAndUnknownDeclaredNamesResolveToNothing()
    {
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(null, "", "   ").Should().BeNull();
        SecurityAssetClassCatalog.ResolveAccountingInstrumentClass("EsotericBasketCertificate").Should().BeNull();
    }
}
