using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application.SecurityMaster;

public sealed class SecurityMasterVersionedConflictTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private const string Calls = "EconomicTerms.callSchedule";

    [Fact]
    public void EquivalentDeclaredSchedules_NormalizeMemberOrderDatesNumericScaleAndEntryOrder()
    {
        var a = Record("A", """
            {"assetClassTermsVersion":3,"callSchedule":[{"callDate":"2028-01-01","callPrice":102.00},{"callDate":"2027-01-01","callPrice":101.00}]}
            """);
        var b = Record("B", """
            {"assetClassTermsVersion":3,"callSchedule":[{"callPrice":101,"callDate":"01/01/2027"},{"callPrice":102,"callDate":"2028-01-01"}]}
            """);

        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
        SecurityMasterConflictDetection.FieldValuesMatch(Calls,
            "[{\"callDate\":\"2027-01-01\",\"callPrice\":101.00}]",
            "[{\"callPrice\":101,\"callDate\":\"2027-01-01\"}]").Should().BeTrue();
    }

    [Fact]
    public void AChangedWindow_OpensStableConflict_AndAttributionUsesSameReadablePath()
    {
        var a = Record("A", """{"assetClassTermsVersion":3,"callSchedule":[{"callDate":"2027-01-01","callEndDate":"2027-06-01"}]}""");
        var b = Record("B", """{"assetClassTermsVersion":3,"callSchedule":[{"callDate":"2027-01-01","callEndDate":"2027-07-01"}]}""");

        var forward = SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().ContainSingle().Which;
        var reverse = SecurityMasterConflictDetection.DetectFieldConflicts(b, a, DateTimeOffset.UnixEpoch).Should().ContainSingle().Which;
        forward.FieldPath.Should().Be(Calls);
        forward.ConflictId.Should().Be(reverse.ConflictId);
        SecurityMasterConflictDetection.ReadComparableFieldValue(a, Calls).Should().Be(forward.ValueA);
        SecurityMasterConflictDetection.ChangedGovernedFieldPaths(a, b).Should().ContainSingle().Which.Should().Be(Calls);
    }

    [Fact]
    public void ConventionAliasesAndJointCalendarOrder_CompareSemantically()
    {
        var a = Record("A", """{"assetClassTermsVersion":2,"scheduleConventions":{"businessDayConvention":"MF","paymentCalendars":["USNY","GBLO"]}}""");
        var b = Record("B", """{"assetClassTermsVersion":2,"scheduleConventions":{"paymentCalendars":["GBLO","USNY","GBLO"],"businessDayConvention":"ModifiedFollowing"}}""");
        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
    }

    [Fact]
    public void ForeignAndMalformedMembers_KeepTheirIdentity()
    {
        var a = Record("A", """{"assetClassTermsVersion":2,"scheduleConventions":{"vendorRule":"A","businessDayConvention":{}}}""");
        var b = Record("B", """{"assetClassTermsVersion":2,"scheduleConventions":{"vendorRule":"a","businessDayConvention":[]}}""");
        var conflict = SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().ContainSingle().Which;
        conflict.FieldPath.Should().Be("EconomicTerms.scheduleConventions");
        conflict.ValueA.Should().Contain("vendorRule");
        SecurityMasterConflictDetection.FieldValuesMatch(conflict.FieldPath, conflict.ValueA, conflict.ValueB).Should().BeFalse();
    }

    [Fact]
    public void DuplicateExerciseStarts_KeepTheirAmbiguousEntryOrder()
    {
        var a = Record("A", """{"assetClassTermsVersion":3,"putSchedule":[{"putDate":"2027-01-01","putPrice":100},{"putDate":"2027-01-01","putPrice":101}]}""");
        var b = Record("B", """{"assetClassTermsVersion":3,"putSchedule":[{"putDate":"2027-01-01","putPrice":101},{"putDate":"2027-01-01","putPrice":100}]}""");
        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().ContainSingle();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[null,{}, {\"callDate\":null}]")]
    public void ProtocolUnsetContainers_CompareAsNoDeclaration(string unset)
    {
        var a = Record("A", $"{{\"assetClassTermsVersion\":3,\"callSchedule\":{unset}}}");
        var b = Record("B", """{"assetClassTermsVersion":3}""");

        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
    }

    [Fact]
    public void UnsetCallRows_AreCompactedBesideDeclaredRows()
    {
        var a = Record("A", """{"assetClassTermsVersion":3,"callSchedule":[null,{}, {"callDate":null},{"callDate":"2027-01-01","callPrice":101}]}""");
        var b = Record("B", """{"assetClassTermsVersion":3,"callSchedule":[{"callDate":"2027-01-01","callPrice":101}]}""");

        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
        SecurityMasterConflictDetection.ReadComparableFieldValue(a, Calls).Should().Be(
            """[{"callDate":"2027-01-01","callPrice":101}]""");
    }

    [Fact]
    public void UndeclaredStrayContainers_AndProfileFields_DoNotBecomeClassTermConflicts()
    {
        var a = Record("A", """{"assetClassTermsVersion":1,"scheduleConventions":{"businessDayConvention":"F"}}""");
        var b = Record("B", """{"assetClassTermsVersion":1,"scheduleConventions":{"businessDayConvention":"P"}}""");
        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
        a = a with { AssetClass = "StructuredCredit", AssetSpecificTerms = JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 4, profileFields = new { }, callSchedule = new[] { new { callDate = "2027-01-01" } } }) };
        b = b with { AssetClass = "StructuredCredit", AssetSpecificTerms = JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 4, profileFields = new { }, callSchedule = new[] { new { callDate = "2028-01-01" } } }) };
        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().BeEmpty();
    }

    [Fact]
    public void SwapLegBlocks_CompareAtTheirDecodedPeriodIndependentPath()
    {
        var a = Record("A", """{"assetClassTermsVersion":2,"legs":[{"legType":"Fixed","fixedRate":0.05,"scheduleConventions":{"couponRateUnit":"Fraction"}}]}""", "Swap");
        var b = Record("B", """{"assetClassTermsVersion":2,"legs":[{"legType":"Fixed","fixedRate":0.05,"scheduleConventions":{"couponRateUnit":"Percent"}}]}""", "Swap");
        SecurityMasterConflictDetection.DetectFieldConflicts(a, b, DateTimeOffset.UnixEpoch).Should().ContainSingle()
            .Which.FieldPath.Should().Be("EconomicTerms.legs.0.scheduleConventions");
    }

    private static SecurityProjectionRecord Record(string source, string terms, string assetClass = "Bond")
        => new(Id, assetClass, SecurityStatusDto.Active, "Conflicting schedule", "USD", "InternalCode", "TEST",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.Deserialize<JsonElement>(terms),
            JsonSerializer.SerializeToElement(new { sourceSystem = source }), 1, DateTimeOffset.UnixEpoch, null, [], []);
}
