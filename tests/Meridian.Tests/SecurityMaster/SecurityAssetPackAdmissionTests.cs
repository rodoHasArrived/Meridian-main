using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetPackAdmissionTests
{
    private static readonly DateTimeOffset EffectiveAt = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registry_ExposesDistinctPerClassWritableContracts()
    {
        var equity = SecurityAssetPackRegistry.Find("public-equity-etf")!.ContractSchema.AssetClassTerms.Single(schema => schema.AssetClass == "Equity");
        var fixedIncome = SecurityAssetPackRegistry.Find("fixed-income")!.ContractSchema.AssetClassTerms.Single(schema => schema.AssetClass == "Bond");
        equity.Fields.Select(field => field.Key).Should().Contain("classification").And.NotContain("maturity");
        fixedIncome.Fields.Select(field => field.Key).Should().Contain("maturity").And.NotContain("classification");
        foreach (var schema in SecurityAssetPackRegistry.All.SelectMany(pack => pack.ContractSchema.AssetClassTerms))
        {
            schema.TermsVersion.Should().Be(SecurityAssetClassCatalog.GetTermsVersion(schema.AssetClass));
            schema.Fields.Should().OnlyContain(field => field.SinceTermsVersion <= schema.TermsVersion);
        }
        SecurityAssetPackRegistry.ValidateAll().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Descriptor_WithAChangedFieldShape_CannotPassAdmissionMetadataValidation()
    {
        var baseline = SecurityAssetPackRegistry.Find("fixed-income")!;
        var schemas = baseline.ContractSchema.AssetClassTerms.ToArray();
        var bondIndex = Array.FindIndex(schemas, schema => schema.AssetClass == "Bond");
        var fields = schemas[bondIndex].Fields.ToArray();
        var maturity = Array.FindIndex(fields, field => field.Key == "maturity");
        fields[maturity] = fields[maturity] with { Type = SecurityAssetTermFieldType.Decimal };
        schemas[bondIndex] = schemas[bondIndex] with { Fields = fields };
        var candidate = baseline with { ContractSchema = baseline.ContractSchema with { AssetClassTerms = schemas } };

        var result = SecurityAssetPackRegistry.ValidateDescriptor(candidate);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == "asset-pack.writable-contract-schema-mismatch");
    }

    [Fact]
    public void CanonicalAdmission_RejectsNestedFieldTypes()
    {
        var record = Record("Equity", new { convertibleTerms = new { underlyingSecurityId = "not-a-guid", conversionRatio = 2m } });
        var action = () => SecurityAssetPackAdmission.EnsureAdmitted(record, EffectiveAt);
        action.Should().ThrowExactly<SecurityMasterWriteRefusedException>().WithMessage("*SM_TERM_TYPE_INVALID*underlyingSecurityId*");
    }

    [Fact]
    public void SubmittedAdmission_RejectsMalformedArrayItemsBeforeDecoding()
    {
        var terms = JsonSerializer.SerializeToElement(new { effectiveDate = "2026-01-01", maturityDate = "2027-01-01", legs = new object[] { "lost-by-a-tolerant-decoder" } });
        var action = () => SecurityAssetPackAdmission.EnsureSubmittedFields("Swap", terms);
        action.Should().Throw<InvalidOperationException>().WithMessage("*SM_TERM_TYPE_INVALID*legs.0*");
    }

    [Fact]
    public void SubmittedAdmission_LeavesOmittedSerializedDefaultsToTheCodec()
    {
        // isAdjusted defaults to false in the typed Option codec; raw input shape validation
        // must not turn that serialized-presence rule into a new caller requirement.
        var terms = JsonSerializer.SerializeToElement(new
        {
            underlyingId = Guid.NewGuid(),
            putCall = "Call",
            strike = 100m,
            expiry = "2027-01-01",
            multiplier = 10m
        });
        var action = () => SecurityAssetPackAdmission.EnsureSubmittedFields("Option", terms);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task CreateAsync_MalformedArrayItemWritesNoEventOrProjection()
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        using var service = new SecurityMasterService(eventStore, snapshots, store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshots), new SecurityMasterOptions(),
            NullLogger<SecurityMasterService>.Instance);
        var request = new CreateSecurityRequest(Guid.NewGuid(), "Swap",
            JsonSerializer.SerializeToElement(new { displayName = "Swap", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { effectiveDate = "2026-01-01", maturityDate = "2027-01-01", legs = new object[] { false } }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SWAP-ADMISSION", true, EffectiveAt)],
            EffectiveAt, "test", "test", null, "test");

        await service.Invoking(instance => instance.CreateAsync(request)).Should().ThrowAsync<Exception>();

        await eventStore.DidNotReceive().AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
    }

    private static SecurityProjectionRecord Record(string assetClass, object terms)
        => new(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, "Admission test", "USD", "Internal", "TEST",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), JsonSerializer.SerializeToElement(new { }),
            1, EffectiveAt, null, [], []);
}
