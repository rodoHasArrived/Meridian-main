using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Formerly mandatory class fields remain mandatory after their shared homes become optional.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityRequiredModuleValueTests
{
    private static readonly (string AssetClass, string Module, string Field)[] Cases =
    [
        ("Option", "Exercise", "UnderlyingSecurityId"), ("Option", "Dates", "MaturityDate"),
        ("Warrant", "Exercise", "UnderlyingSecurityId"), ("Future", "Dates", "MaturityDate"),
        ("Bond", "Dates", "MaturityDate"), ("CertificateOfDeposit", "Dates", "MaturityDate"),
        ("CommercialPaper", "Dates", "MaturityDate"), ("TreasuryBill", "Dates", "MaturityDate"),
        ("Repo", "FinancingTerms", "OpenDate"), ("Repo", "FinancingTerms", "CloseDate"),
        ("Swap", "Dates", "EffectiveDate"), ("Swap", "Dates", "MaturityDate"),
        ("CommitmentGuarantee", "Dates", "EffectiveDate"), ("FxForward", "Dates", "MaturityDate"),
        ("NonDeliverableForward", "Dates", "EffectiveDate"), ("NonDeliverableForward", "Dates", "MaturityDate"),
        ("CreditDefaultSwap", "Dates", "EffectiveDate"), ("CreditDefaultSwap", "Dates", "MaturityDate"),
        ("Swaption", "Dates", "MaturityDate"), ("Swaption", "Exercise", "StrikeRate"),
        ("CapFloor", "Dates", "EffectiveDate"), ("CapFloor", "Dates", "MaturityDate"),
        ("StructuredNote", "Dates", "MaturityDate"), ("PrivateFundInterest", "PrivateAssetValuation", "NavDate"),
        ("RealEstateHolding", "PrivateAssetValuation", "ValuationDate")
    ];

    internal static IReadOnlySet<string> RequiredShapeClasses { get; } = Cases.Select(static row => row.AssetClass).ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string, string, string> RequiredCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in Cases)
            data.Add(row.AssetClass, row.Module, row.Field);
        return data;
    }

    [Theory]
    [MemberData(nameof(RequiredCases))]
    public void MissingRequiredModuleValue_RefusesCreateAndAmendBeforeSnapshotSerialization(string assetClass, string module, string field)
    {
        var current = Record(assetClass);
        var malformed = ReplaceModuleValue(current.Kind, module, field, null);
        var definition = SecurityMasterLegacyUpgrade.modulesFromKind(malformed);
        SecurityMasterCommandFacade.ValidateDefinitionInvariants(assetClass, definition)
            .Select(static error => error.Code).Should().Contain("required_module_value_missing");
        SecurityMasterCommandFacade.ValidateKindInvariants(malformed)
            .Select(static error => error.Code).Should().Contain("required_module_value_missing");

        var created = SecurityMasterCommandFacade.Create(Create(current, malformed));
        created.IsSuccess.Should().BeFalse();
        created.ErrorDetails.Select(static error => error.Code).Should().Contain("required_module_value_missing");
        var amended = SecurityMasterCommandFacade.Amend(current, new AmendTerms(
            current.SecurityId, current.Version, null, FSharpOption<SecurityKind>.Some(malformed),
            FSharpList<Identifier>.Empty, FSharpList<Identifier>.Empty, current.EffectiveFrom.AddDays(1), current.Provenance));
        amended.IsSuccess.Should().BeFalse();
        amended.ErrorDetails.Select(static error => error.Code).Should().Contain("required_module_value_missing");
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("Warrant")]
    public void PresentEmptyGuid_KeepsTheOldUnderlyingValueSemantics(string assetClass)
    {
        var record = Record(assetClass);
        var kind = ReplaceModuleValue(record.Kind, "Exercise", "UnderlyingSecurityId", FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(Guid.Empty)));
        var result = SecurityMasterCommandFacade.Create(Create(record, kind));
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        using var flat = JsonDocument.Parse(result.Snapshot.AssetSpecificTermsJson);
        flat.RootElement.GetProperty("underlyingId").GetGuid().Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("Option", "IsAdjusted", "isAdjusted")]
    [InlineData("Future", "IsRollTarget", "isRollTarget")]
    public void UndeclaredListingFlags_UseOnlyTheFrozenOptionalJsonDefault(string assetClass, string field, string jsonKey)
    {
        var record = Record(assetClass);
        var kind = ReplaceModuleValue(record.Kind, "ListingDetail", field, null);
        var result = SecurityMasterCommandFacade.Create(Create(record, kind));
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        using var flat = JsonDocument.Parse(result.Snapshot.AssetSpecificTermsJson);
        flat.RootElement.GetProperty(jsonKey).GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void PresentZeroSwaptionStrike_KeepsTheOldSignedRateSemantics()
    {
        var record = Record("Swaption");
        var kind = ReplaceModuleValue(record.Kind, "Exercise", "StrikeRate", FSharpOption<decimal>.Some(0m));
        var result = SecurityMasterCommandFacade.Create(Create(record, kind));
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        using var flat = JsonDocument.Parse(result.Snapshot.AssetSpecificTermsJson);
        flat.RootElement.GetProperty("strikeRate").GetDecimal().Should().Be(0m);
    }

    private static CreateSecurity Create(SecurityMasterRecord record, SecurityKind kind)
        => new(record.SecurityId, record.Common, record.Identifiers, kind, record.EffectiveFrom, record.Provenance);

    private static SecurityMasterRecord Record(string assetClass)
        => SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])));

    private static SecurityKind ReplaceModuleValue(SecurityKind kind, string module, string field, object? replacement)
    {
        var union = FSharpValue.GetUnionFields(kind, typeof(SecurityKind), null);
        var payload = union.Item2.Single();
        var moduleValue = payload.GetType().GetProperty(module)!.GetValue(payload)!;
        var changedModule = ReplaceRecordField(moduleValue, field, replacement);
        var changedPayload = ReplaceRecordField(payload, module, changedModule);
        return (SecurityKind)FSharpValue.MakeUnion(union.Item1, [changedPayload], null);
    }

    private static object ReplaceRecordField(object record, string name, object? replacement)
    {
        var fields = FSharpType.GetRecordFields(record.GetType(), null);
        var values = FSharpValue.GetRecordFields(record, null);
        var index = Array.FindIndex(fields, field => field.Name == name);
        index.Should().BeGreaterThanOrEqualTo(0, $"the typed record must store {name}");
        values[index] = replacement!;
        return FSharpValue.MakeRecord(record.GetType(), values, null);
    }
}
