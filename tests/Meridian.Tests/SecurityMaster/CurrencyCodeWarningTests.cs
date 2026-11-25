using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The ISO 4217 currency check (review Priority 1, step 3) ships as a Warning: it reports codes that
/// are not ISO 4217 or an accepted market extension, and blocks nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CurrencyCodeWarningTests
{
    private const string Code = "SM_CURRENCY_NOT_ISO4217";
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);

    private static readonly SecurityValidationService Service = new(
        Substitute.For<ISecurityMasterStore>(),
        AssetClassValidatorRegistry.CreateDefault());

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("XAU")]
    [InlineData("CNH")]
    [InlineData("GBX")]
    public void RecognisedRecordCurrency_IsNotReported(string currency)
        => Issues(Record("Equity", currency, new { })).Should().NotContain(i => i.Code == Code);

    [Theory]
    [InlineData("US")]
    [InlineData("usd")]
    [InlineData("ABC")]
    [InlineData("HRK")]
    [InlineData("XXX")]
    public void UnrecognisedRecordCurrency_IsAWarningOnTheCurrencyField(string currency)
    {
        var issue = Issues(Record("Equity", currency, new { })).Should().ContainSingle(i => i.Code == Code).Subject;

        issue.Severity.Should().Be(SecurityValidationSeverityDto.Warning);
        issue.AffectedFields.Should().ContainSingle().Which.Should().Be("currency");
    }

    [Fact]
    public void BlankRecordCurrency_IsLeftToTheRequiredRule()
        => Issues(Record("Equity", "", new { })).Should().NotContain(i => i.Code == Code);

    [Fact]
    public void FxPairAndSettlementCurrencies_AreChecked()
    {
        var terms = new { baseCurrency = "USD", quoteCurrency = "BRX", settlementCurrency = "usd", fixingDate = "2027-01-15", valueDate = "2027-01-19" };

        Issues(Record("NonDeliverableForward", "USD", terms)).Should().ContainSingle(i => i.Code == Code)
            .Which.AffectedFields.Should().BeEquivalentTo("assetSpecificTerms.quoteCurrency", "assetSpecificTerms.settlementCurrency");
    }

    [Fact]
    public void CryptoPairCodes_AreNotChecked_ButTheRecordCurrencyIs()
    {
        var terms = new { baseCurrency = "BTC", quoteCurrency = "USDT" };

        Issues(Record("CryptoCurrency", "USD", terms)).Should().NotContain(i => i.Code == Code);
        Issues(Record("CryptoCurrency", "USDT", terms)).Should().ContainSingle(i => i.Code == Code)
            .Which.AffectedFields.Should().ContainSingle().Which.Should().Be("currency");
    }

    [Fact]
    public void SwapLegCurrencies_AreChecked_ThroughTheLegIndex()
    {
        var terms = new
        {
            effectiveDate = "2026-12-15",
            maturityDate = "2031-12-15",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 3.85m, exchangesPrincipal = false },
                new { legType = "Floating", currency = "EURO", index = "EURIBOR", exchangesPrincipal = false }
            }
        };

        Issues(Record("Swap", "USD", terms)).Should().ContainSingle(i => i.Code == Code)
            .Which.AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.legs.1.currency");
    }

    [Fact]
    public void MarketExtensions_AreKeptApartFromIsoCodes()
    {
        Iso4217Currencies.Codes.Should().NotContain(Iso4217Currencies.MarketExtensions);
        Iso4217Currencies.Codes.Should().OnlyContain(code => code.Length == 3 && code.All(char.IsAsciiLetterUpper));
    }

    private static IReadOnlyList<SecurityValidationIssueDto> Issues(SecurityProjectionRecord record)
        => Service.ValidateRecord(record, [record], Now).Issues;

    private static SecurityProjectionRecord Record(string assetClass, string currency, object terms)
        => new(
            Guid.NewGuid(),
            assetClass,
            SecurityStatusDto.Active,
            "Test security",
            currency,
            "Internal",
            "TEST",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(terms),
            JsonSerializer.SerializeToElement(new { }),
            1,
            Now.AddYears(-1),
            null,
            [],
            []);
}
