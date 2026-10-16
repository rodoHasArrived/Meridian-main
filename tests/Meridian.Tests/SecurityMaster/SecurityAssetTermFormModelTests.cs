using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Schema-driven editor rules built from the registry definition: which terms are pickable, which
/// offer a choice list, and which values are rejected before staging.
/// </summary>
public sealed class SecurityAssetTermFormModelTests
{
    [Fact]
    public void PickableFields_AreTheScalarTermsOfTheRegistryDefinition()
    {
        var bond = SecurityAssetClassRegistry.ToDto(SecurityAssetClassRegistry.Get("Bond"));

        var pickable = SecurityAssetTermFormModel.PickableFields(bond);

        pickable.Should().NotBeEmpty().And.OnlyContain(static field => field.ScalarEditable);
        pickable.Select(static field => field.Order).Should().BeInAscendingOrder();
        pickable.Should().NotContain(static field => field.Type == "Array" || field.Type == "Object");
        SecurityAssetTermFormModel.PickableFields(null).Should().BeEmpty();
    }

    [Fact]
    public void ConventionAndChoiceTerms_OfferTheirVocabulary()
    {
        var option = SecurityAssetClassRegistry.ToDto(SecurityAssetClassRegistry.Get("Option"));
        var settlement = option.Fields.Single(static field => field.Key == "settlementType");

        SecurityAssetTermFormModel.ValueChoices(settlement).Should().NotBeEmpty();
        SecurityAssetTermFormModel.ValueChoices(Field(SecurityAssetTermEditorKind.Toggle)).Should().Equal("true", "false");
        SecurityAssetTermFormModel.ValueChoices(Field(SecurityAssetTermEditorKind.Text)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAssetTermEditorKind.Decimal, "5.125", null)]
    [InlineData(SecurityAssetTermEditorKind.Decimal, "abc", "must be a number")]
    [InlineData(SecurityAssetTermEditorKind.Integer, "1.5", "whole number")]
    [InlineData(SecurityAssetTermEditorKind.Date, "2031-01-15", null)]
    [InlineData(SecurityAssetTermEditorKind.Date, "15/01/2031", "YYYY-MM-DD")]
    [InlineData(SecurityAssetTermEditorKind.Toggle, "yes", "true or false")]
    [InlineData(SecurityAssetTermEditorKind.Choice, "C", "one of: A, B")]
    [InlineData(SecurityAssetTermEditorKind.Convention, "anything the server parses", null)]
    public void Validate_PreChecksTypedValues(SecurityAssetTermEditorKind editor, string value, string? expected)
    {
        var message = SecurityAssetTermFormModel.Validate(Field(editor), value);

        if (expected is null)
        {
            message.Should().BeNull();
        }
        else
        {
            message.Should().Contain(expected);
        }
    }

    [Fact]
    public void Validate_RequiresRequiredTerms_AndIgnoresAnUnpickedField()
    {
        SecurityAssetTermFormModel.Validate(Field(SecurityAssetTermEditorKind.Text) with { Required = true }, " ")
            .Should().Be("Term is required.");
        SecurityAssetTermFormModel.Validate(null, "anything").Should().BeNull();
    }

    private static SecurityAssetTermFieldDefinitionDto Field(SecurityAssetTermEditorKind editor)
        => new("term", "assetSpecificTerms.term", "Term", "String", editor, "Terms", 0, false, true, ["A", "B"], []);
}
