using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts.Ledger;

public sealed class LedgerAgreementDimensionsTests
{
    [Fact]
    public void AbsentReference_PreservesLegacySerializationAndSignature()
    {
        var dimensions = new LedgerDimensionSetDto(EntityId: "entity-a");
        JsonSerializer.Serialize(dimensions).Should().NotContain("IntercompanyAgreement");
        LedgerAgreementDimensions.AppendSignature("legacy|scope", null, null).Should().Be("legacy|scope");
    }

    [Fact]
    public void PopulatedReference_RoundTripsAndCountsAsDimension()
    {
        var dimensions = new LedgerDimensionSetDto
        {
            IntercompanyAgreementId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            IntercompanyAgreementVersion = 2
        };
        var restored = JsonSerializer.Deserialize<LedgerDimensionSetDto>(JsonSerializer.Serialize(dimensions))!;
        restored.IntercompanyAgreementId.Should().Be(dimensions.IntercompanyAgreementId);
        restored.IntercompanyAgreementVersion.Should().Be(2);
        LedgerDimensionTags.HasAnyDimension(restored).Should().BeTrue();
    }

    [Fact]
    public void PrefixedLineTags_ReadTheExactApprovedReference()
    {
        var id = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        const string prefix = "lineDimensions.11111111111111111111111111111111.";
        var tags = new Dictionary<string, string>
        {
            [prefix + "INTERCOMPANYAGREEMENTID"] = " " + id.ToString("D") + " ",
            [prefix + "intercompanyAgreementVersion"] = " 3 "
        };
        LedgerAgreementDimensions.ReadId(tags, prefix).Should().Be(id);
        LedgerAgreementDimensions.ReadVersion(tags, prefix).Should().Be(3);
        LedgerAgreementDimensions.ReadId(tags).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void SuppliedInvalidId_IsNotTreatedAsAnAbsentDimension(string value)
    {
        var tags = new Dictionary<string, string> { ["intercompanyAgreementId"] = value };
        Action read = () => LedgerAgreementDimensions.ReadId(tags);
        read.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void SuppliedInvalidVersion_IsNotTreatedAsAnAbsentDimension(string value)
    {
        var tags = new Dictionary<string, string> { ["intercompanyAgreementVersion"] = value };
        Action read = () => LedgerAgreementDimensions.ReadVersion(tags);
        read.Should().Throw<ArgumentException>();
    }
}
