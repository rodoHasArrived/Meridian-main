using System.Text.Json;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts.Ledger;

public sealed class IntercompanyFxTransportTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ApprovedRateSourceGeneratedJson_PreservesAllDecimalDigitsAsText()
    {
        const decimal exact = 0.1234567890123456789012345678m;
        var value = new ConsolidationFxRateSetDto(Guid.NewGuid(), 1, "tenant", "company",
            [new(Guid.NewGuid(), "EUR", "USD", new(2026, 6, 30), exact,
                ConsolidationFxRateKindDto.Closing, "vault:synthetic-rate")], "vault:synthetic-set", "owner", "owner", DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("rates")[0].GetProperty("rate").ValueKind);
        Assert.Equal("0.1234567890123456789012345678", document.RootElement.GetProperty("rates")[0].GetProperty("rate").GetString());
        Assert.Equal(exact, JsonSerializer.Deserialize(json, ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto)!.Rates[0].Rate);
    }

    [Fact]
    public void HistoricalOpeningSourceGeneratedJson_PreservesLargeSignedAmount()
    {
        const decimal opening = -12345678901234567890.123456789m;
        var value = new ConsolidationFxBookConfigurationDto(Guid.NewGuid(), 1, "tenant", "company", new(2026, 1, 1),
            null, Guid.NewGuid(), 1, new("current-rate", "v1", "USD", 2), "owner", DateTimeOffset.UnixEpoch)
        {
            HistoricalFacts = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, opening, "vault:opening")]
        };
        var json = JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("historicalFacts")[0].GetProperty("openingTranslatedSignedAmount").ValueKind);
        Assert.Equal(opening, JsonSerializer.Deserialize(json, ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto)!
            .HistoricalFacts![0].OpeningTranslatedSignedAmount);
    }

    [Fact]
    public void ManualOriginalCurrency_UsesExactTextForAmountAndRate()
    {
        var value = new ManualJournalLineCurrencyDto("EUR", 9007199254740993.01m, 0.1234567890123456789012345678m);
        var json = JsonSerializer.Serialize(value, Web);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("9007199254740993.01", document.RootElement.GetProperty("amount").GetString());
        Assert.Equal("0.1234567890123456789012345678", document.RootElement.GetProperty("fxRateToFunctional").GetString());
        Assert.Equal(value, JsonSerializer.Deserialize<ManualJournalLineCurrencyDto>(json, Web));
    }

    [Fact]
    public void ManualOriginalCurrency_StillReadsRetainedNumericPayloads()
    {
        const string json = """{"currency":"EUR","amount":9007199254740993.01,"fxRateToFunctional":1.1234567890123456789}""";
        var value = JsonSerializer.Deserialize<ManualJournalLineCurrencyDto>(json, Web)!;
        Assert.Equal(9007199254740993.01m, value.Amount);
        Assert.Equal(1.1234567890123456789m, value.FxRateToFunctional);
    }

    [Fact]
    public void LegacyJournalLine_DoesNotAcquireAnEmptyOriginalCurrencyField()
    {
        const string legacy = """{"lineId":"line-1","side":"Debit","amount":100,"currency":"USD","accountPath":"Assets:Cash"}""";
        var value = JsonSerializer.Deserialize<ManualJournalEntryLineDto>(legacy, Web)!;
        Assert.Null(value.TransactionCurrency);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, Web));
        Assert.False(document.RootElement.TryGetProperty("transactionCurrency", out _));
    }

    [Fact]
    public void LegacyDimensions_DoNotAcquireNullIntercompanyFields()
    {
        var value = JsonSerializer.Deserialize<LedgerDimensionSetDto>("""{"entityId":"retained-entity"}""", Web)!;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, Web));
        foreach (var property in new[] { "agreementId", "agreementRevisionId", "chartAccountNodeId", "accountBindingRevisionId", "intercompanyTransactionId" })
            Assert.False(document.RootElement.TryGetProperty(property, out _));
    }

    [Fact]
    public void LegacyPostingCommand_DoesNotAcquireNullFxAuthority()
    {
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 30),
            DateTimeOffset.UnixEpoch, "legacy-posting");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command, Web));
        Assert.False(document.RootElement.TryGetProperty("intercompanyFx", out _));
    }
}
