using Meridian.Storage.Archival;
using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementCurrencyCatalogEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "statement-currency-catalog-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("AAA")]
    [InlineData("XXX")]
    [InlineData("XTS")]
    public async Task DirectCanonicalImport_UnrecognizedCurrencyRefusesBeforeRetention(string currency)
    {
        var (service, store, request) = await CreateImportAsync(currency);

        var validation = await service.ValidateAsync(request);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain("recognized currency code");
        var rejection = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(request));
        rejection.Message.Should().Contain("recognized currency code");
        (await store.ListImportsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(" usd ", "USD")]
    [InlineData("HRK", "HRK")]
    [InlineData("CNH", "CNH")]
    [InlineData("XCG", "XCG")]
    [InlineData("BOV", "BOV")]
    [InlineData("CHE", "CHE")]
    [InlineData("CHW", "CHW")]
    [InlineData("COU", "COU")]
    [InlineData("MXV", "MXV")]
    [InlineData("USN", "USN")]
    [InlineData("UYW", "UYW")]
    [InlineData("XAD", "XAD")]
    [InlineData("XBA", "XBA")]
    [InlineData("XBB", "XBB")]
    [InlineData("XBC", "XBC")]
    [InlineData("XBD", "XBD")]
    public async Task DirectCanonicalImport_RecognizedCurrentHistoricalAndOperationalCurrenciesPreserveAmounts(
        string sourceCurrency, string expectedCurrency)
    {
        var (service, _, request) = await CreateImportAsync(sourceCurrency);

        var validation = await service.ValidateAsync(request);
        validation.Errors.Should().BeEmpty();
        validation.IsValid.Should().BeTrue();
        var result = await service.ImportAsync(request);

        var row = result.Rows.Should().ContainSingle().Subject;
        row.Currency.Should().Be(expectedCurrency);
        row.CashAmount.Should().Be(-25.5m);
    }

    private async Task<(CsvBrokerStatementService Service, JsonCanonicalStatementStore Store, BrokerStatementImportRequest Request)>
        CreateImportAsync(string currency)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "source.csv");
        await File.WriteAllTextAsync(path,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\n"
            + $"FUND-A,,0,0,-25.5,fee,2026-06-02,,{currency}\n");
        var store = new JsonCanonicalStatementStore(_root, new AtomicFileWriterAdapter());
        var service = new CsvBrokerStatementService(store);
        var request = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 6, 30))
        {
            ExternalAccountId = "FUND-A"
        };
        return (service, store, request);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
