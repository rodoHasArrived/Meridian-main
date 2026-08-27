using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// CSV bulk import produced requests whose common-terms and asset-specific-terms payloads were both
/// the empty document, so every row failed at create time with "Missing required string 'displayName'"
/// — for every asset class, on a path wired to both the desktop workstation and the HTTP import
/// endpoint. These lock the payload the parser must carry, and the classes it may accept.
/// </summary>
public sealed class SecurityMasterCsvParserTests
{
    private const string Header = "Ticker,Name,AssetClass,Currency,Exchange,ISIN,CUSIP,FIGI";

    [Fact]
    public void ParsedRow_CarriesTheDisplayNameAndCurrencyTheCreatePathRequires()
    {
        var parsed = Parse($"{Header}\nAAPL,Apple Inc.,Equity,USD,XNAS,US0378331005,037833100,BBG000B9XRY4");

        var request = parsed.Should().ContainSingle().Subject;
        request.AssetClass.Should().Be("Equity");
        ReadString(request.CommonTerms, "displayName").Should().Be("Apple Inc.");
        ReadString(request.CommonTerms, "currency").Should().Be("USD");
        ReadString(request.CommonTerms, "exchange").Should().Be("XNAS");
    }

    [Fact]
    public void ParsedRow_StampsTheAssetSpecificTermsSchemaVersion()
    {
        var parsed = Parse($"{Header}\nAAPL,Apple Inc.,Equity,USD,,,,");

        var assetSpecificTerms = parsed.Should().ContainSingle().Subject.AssetSpecificTerms;
        assetSpecificTerms.GetProperty("schemaVersion").GetInt32()
            .Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
    }

    [Fact]
    public void ParsedRow_DefaultsCurrencyAndOmitsAbsentExchange()
    {
        var parsed = Parse($"{Header}\nAAPL,Apple Inc.,Equity,,,,,");

        var commonTerms = parsed.Should().ContainSingle().Subject.CommonTerms;
        ReadString(commonTerms, "currency").Should().Be("USD");
        commonTerms.TryGetProperty("exchange", out _).Should().BeFalse(
            "an absent exchange column must not be persisted as an empty string");
    }

    [Fact]
    public void ParsedRow_CarriesEverySuppliedIdentifier()
    {
        var parsed = Parse($"{Header}\nAAPL,Apple Inc.,Equity,USD,XNAS,US0378331005,037833100,BBG000B9XRY4");

        var identifiers = parsed.Should().ContainSingle().Subject.Identifiers;
        identifiers.Should().HaveCount(4);
        identifiers.Should().ContainSingle(identifier =>
            identifier.Kind == SecurityIdentifierKind.Ticker && identifier.IsPrimary);
        identifiers.Select(identifier => identifier.Kind).Should().BeEquivalentTo(new[]
        {
            SecurityIdentifierKind.Ticker,
            SecurityIdentifierKind.Isin,
            SecurityIdentifierKind.Cusip,
            SecurityIdentifierKind.Figi
        });
    }

    [Fact]
    public void EveryImportableAssetClassIsAccepted()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.IdentifierOnlyImportableAssetClasses)
        {
            var parsed = new SecurityMasterCsvParser()
                .Parse($"{Header}\nSYM,Some security,{assetClass},USD,,,,", out var errors);

            errors.Should().BeEmpty($"'{assetClass}' declares SupportsIdentifierOnlyImport");
            parsed.Should().ContainSingle().Which.AssetClass.Should().Be(assetClass);
        }
    }

    [Fact]
    public void AClassNeedingAssetSpecificTermsIsRefusedByName()
    {
        // Option needs an underlying, put/call, strike, expiry and multiplier. A CSV row carries
        // none of them, and defaulting them would mint a governed record on invented economics.
        new SecurityMasterCsvParser()
            .Parse($"{Header}\nAAPL240621C00150000,Apple call,Option,USD,,,,", out var errors);

        errors.Should().ContainSingle().Which
            .Should().Contain("requires asset-specific terms");
    }

    [Fact]
    public void AnUnknownClassIsRefusedAsUnknown()
    {
        new SecurityMasterCsvParser()
            .Parse($"{Header}\nSYM,Some security,TokenizedCarbonCredit,USD,,,,", out var errors);

        errors.Should().ContainSingle().Which
            .Should().Contain("Unknown AssetClass");
    }

    [Fact]
    public void RefusedRowsDoNotBlockTheRestOfTheFile()
    {
        var parsed = Parse(
            $"{Header}\n"
            + "AAPL,Apple Inc.,Equity,USD,,,,\n"
            + "SPY,SPDR S&P 500,Option,USD,,,,\n"
            + "VTI,Vanguard Total Market,InvestmentFund,USD,,,,",
            expectedErrors: 1);

        parsed.Select(request => request.AssetClass)
            .Should().BeEquivalentTo(new[] { "Equity", "InvestmentFund" });
    }

    private static IReadOnlyList<CreateSecurityRequest> Parse(string csv, int expectedErrors = 0)
    {
        var parsed = new SecurityMasterCsvParser().Parse(csv, out var errors);
        errors.Should().HaveCount(expectedErrors, string.Join("; ", errors));
        return parsed;
    }

    private static string? ReadString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
