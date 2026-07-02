using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class StatementMappingProfileLoaderTests
{
    private static StatementMappingProfileDocument ValidDocument => new(
        SchemaVersion: 1,
        ProfileId: "acme-custodian-csv-v1",
        DisplayName: "Acme Custodian CSV",
        Format: "csv",
        Csv: new StatementProfileCsvOptions(",", "\"", HasHeader: true),
        Culture: "en-US",
        DateFormats: ["MM/dd/yyyy"],
        Fields:
        [
            new("Account", "Acct #", ["Account Number"], Required: true),
            new("SecurityIdentifier", "Symbol"),
            new("Quantity", "Qty"),
            new("CashAmount", "Net"),
            new("ActivityType", "Code", Aliases: null, Required: true),
            new("TradeDate", "Trade Dt", Aliases: null, Required: true)
        ],
        ActivityCodes: [new("BOT", "trade"), new("SLD", "trade")]);

    [Fact]
    public void Validate_ValidDocument_ReturnsNoErrors()
    {
        StatementMappingProfileLoader.Validate(ValidDocument).Should().BeEmpty();
    }

    [Fact]
    public void ToRuntimeProfile_MaterializesFieldsAndActivityCodes()
    {
        var profile = StatementMappingProfileLoader.ToRuntimeProfile(ValidDocument);

        profile.ProfileId.Should().Be("acme-custodian-csv-v1");
        profile.FindField(StatementCanonicalField.Account)!.SourceColumn.Should().Be("Acct #");
        profile.FindField(StatementCanonicalField.Account)!.Required.Should().BeTrue();
        profile.FindField(StatementCanonicalField.Quantity)!.Required.Should().BeFalse();
        profile.MapActivityType("bot").Should().Be("trade");
        profile.MapActivityType("unknown-code").Should().Be("unknown-code");
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_Fails()
    {
        var errors = StatementMappingProfileLoader.Validate(ValidDocument with { SchemaVersion = 99 });

        errors.Should().ContainSingle(error => error.Contains("schema version 99"));
    }

    [Fact]
    public void Validate_UnknownCanonicalField_Fails()
    {
        var document = ValidDocument with
        {
            Fields = [.. ValidDocument.Fields, new StatementProfileFieldMapping("NotAField", "X")]
        };

        StatementMappingProfileLoader.Validate(document)
            .Should().ContainSingle(error => error.Contains("NotAField"));
    }

    [Fact]
    public void Validate_DuplicateCanonicalFieldOrColumn_Fails()
    {
        var document = ValidDocument with
        {
            Fields = [.. ValidDocument.Fields, new StatementProfileFieldMapping("Account", "Acct #", null, true)]
        };

        var errors = StatementMappingProfileLoader.Validate(document);

        errors.Should().Contain(error => error.Contains("mapped more than once") && error.Contains("Account"));
        errors.Should().Contain(error => error.Contains("Acct #"));
    }

    [Fact]
    public void Validate_MissingMandatoryField_Fails()
    {
        var document = ValidDocument with
        {
            Fields = ValidDocument.Fields.Where(field => field.CanonicalField != "TradeDate").ToArray()
        };

        StatementMappingProfileLoader.Validate(document)
            .Should().ContainSingle(error => error.Contains("TradeDate"));
    }

    [Fact]
    public void Validate_UnsupportedFormat_Fails()
    {
        StatementMappingProfileLoader.Validate(ValidDocument with { Format = "xlsx" })
            .Should().ContainSingle(error => error.Contains("xlsx"));
    }

    [Fact]
    public void BuiltInProfiles_AllValidate()
    {
        foreach (var document in StatementBuiltInProfiles.All)
        {
            StatementMappingProfileLoader.Validate(document)
                .Should().BeEmpty($"built-in profile '{document.ProfileId}' must be valid");
        }
    }
}
