using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityVersionedScalarCodecCompatibilityTests
{
    [Fact]
    public void StructuredCreditAbsentVersionTwoScalars_RetainHistoricalExplicitNullBytes()
    {
        // This class predates the CD v6 scalar addition. Its writer has always emitted these
        // nullable tranche fields. The CD absent-field filter must not alter unrelated writers.
        var terms = JsonSerializer.SerializeToElement(new
        {
            termsFormat = SecurityTermsFormats.Flat,
            assetClassTermsVersion = 2,
            collateralType = "CLO",
            couponOrIndex = "5",
            originalFace = 100000,
            maturity = "2030-01-01",
            tranche = "A"
        });
        var common = JsonSerializer.SerializeToElement(new { displayName = "Synthetic legacy tranche", currency = "USD" });
        var projection = new SecurityProjectionRecord(
            Guid.Parse("5dafc469-43a1-4b2b-a7b1-c8928e8366f3"), "StructuredCredit", SecurityStatusDto.Active,
            "Synthetic legacy tranche", "USD", "Internal", "SYNTHETIC-TRANCHE", common, terms,
            JsonSerializer.SerializeToElement(new { sourceSystem = "Synthetic", updatedBy = "Synthetic", asOf = "2026-01-01T00:00:00Z" }), 1,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, [], []);

        var written = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)).AssetSpecificTermsJson;

        const string expected = "{\"attachmentPoint\":null,\"collateralType\":\"CLO\",\"couponOrIndex\":\"5\","
            + "\"currentFactor\":null,\"detachmentPoint\":null,\"factorSchedule\":null,\"factorScheduleEntries\":[],"
            + "\"maturity\":\"2030-01-01\",\"nonCallPeriodEnd\":null,\"originalFace\":100000,\"poolId\":null,"
            + "\"reinvestmentPeriodEnd\":null,\"schemaVersion\":1,\"termsFormat\":\"flat\",\"assetClassTermsVersion\":2,"
            + "\"seniority\":null,\"structuredCreditKind\":null,\"tranche\":\"A\",\"weightedAverageCoupon\":null,"
            + "\"weightedAverageLoanAgeMonths\":null,\"weightedAverageMaturityMonths\":null}";
        written.Should().Be(expected);
    }
}
