using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementImportAndMatchingTests
{
    [Fact]
    public async Task Import_is_idempotent_by_duplicate_key()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(path, "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,10,500,5000,BUY,2026-01-02,,USD\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var req = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)) with
        {
            ExternalAccountId = "A1"
        };

        await service.ImportAsync(req);
        await Assert.ThrowsAsync<StatementAlreadyImportedException>(() => service.ImportAsync(req));
    }

    [Fact]
    public async Task Validation_fails_for_bad_header()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "bad.csv");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "foo,bar\n1,2\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var result = await service.ValidateAsync(new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Statement_run_request_hashes_file_contents_and_uses_period_duplicate_key()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "statement-a.csv");
        var secondPath = Path.Combine(root, "statement-b.csv");
        const string content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,10,500,5000,BUY,2026-01-02,,USD\n";
        await File.WriteAllTextAsync(firstPath, content);
        await File.WriteAllTextAsync(secondPath, content);

        var first = await StatementRunCreateRequest.FromFileAsync(
            "samplebroker",
            "samplecustodian",
            "fund-account-1",
            "A1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            firstPath,
            "sample-mapping",
            "default-tolerance",
            "operator@example.test");
        var second = await StatementRunCreateRequest.FromFileAsync(
            "samplebroker",
            "samplecustodian",
            "fund-account-1",
            "A1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            secondPath,
            "sample-mapping",
            "default-tolerance",
            "operator@example.test");

        Assert.Equal(first.SourceFileHash, second.SourceFileHash);
        Assert.Equal(first.DuplicateKey, second.DuplicateKey);
        Assert.DoesNotContain(firstPath, first.SourceFileHash);
    }

    [Fact]
    public async Task Import_detects_duplicates_by_fund_account_period_and_source_file_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "statement-a.csv");
        var secondPath = Path.Combine(root, "statement-b.csv");
        const string content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,10,500,5000,BUY,2026-01-02,,USD\n";
        await File.WriteAllTextAsync(firstPath, content);
        await File.WriteAllTextAsync(secondPath, content);

        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var first = await StatementRunCreateRequest.FromFileAsync(
            "samplebroker",
            "samplecustodian",
            "fund-account-1",
            "A1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            firstPath,
            "sample-mapping",
            "default-tolerance",
            "operator@example.test");
        var second = await StatementRunCreateRequest.FromFileAsync(
            "samplebroker",
            "samplecustodian",
            "fund-account-1",
            "A1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            secondPath,
            "sample-mapping",
            "default-tolerance",
            "operator@example.test");

        var imported = await service.ImportAsync(first.ToBrokerStatementImportRequest());

        Assert.Equal(first.SourceFileHash, imported.Import.SourceFileHash);
        Assert.Equal(first.DuplicateKey, imported.Import.DuplicateKey);
        await Assert.ThrowsAsync<StatementAlreadyImportedException>(() => service.ImportAsync(second.ToBrokerStatementImportRequest()));
    }

    [Fact]
    public async Task Import_captures_optional_currency_and_external_id_columns()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(
            path,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n"
            + "A1,SPY,10,500,-5000,BUY,2026-01-02,2026-01-04,EUR,1.5,EXT-42\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));

        var imported = await service.ImportAsync(
            new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)) with
            {
                ExternalAccountId = "A1"
            });

        var row = Assert.Single(imported.Rows);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal(new DateOnly(2026, 1, 4), row.SettlementDate);
        Assert.Equal(1.5m, row.FeesCommission);
        Assert.Equal("EXT-42", row.ExternalTransactionId);
    }

    [Fact]
    public async Task Import_rejects_a_stale_caller_hash_without_persisting_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "statement.csv");
        const string original =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\n" +
            "A1,SPY,10,500,5000,BUY,2026-01-02,,USD\n";
        const string changed =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\n" +
            "A1,SPY,11,500,5500,BUY,2026-01-02,,USD\n";
        await File.WriteAllTextAsync(path, original);
        var staleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
        await File.WriteAllTextAsync(path, changed);
        var store = new JsonCanonicalStatementStore(root);
        var service = new CsvBrokerStatementService(store);
        var request = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31))
        {
            ExternalAccountId = "A1",
            SourceFileHash = staleHash
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(request));

        Assert.Empty(await store.ListImportsAsync());
    }

    [Fact]
    public async Task Import_parses_the_same_quoted_bytes_used_for_the_authoritative_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "statement.csv");
        const string content =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n" +
            "A1,\"BRK,B\",1,500,-500,\"OTHER \"\"SPECIAL\"\"\",2026-01-02,,USD,,\"TX,\"\"1\"\"\"\n";
        await File.WriteAllTextAsync(path, content);
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));

        var imported = await service.ImportAsync(
            new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31))
            {
                ExternalAccountId = "A1"
            });

        imported.Import.SourceFileHash.Should().Be(
            Sha256Digest.Compute(await File.ReadAllBytesAsync(path)));
        imported.Rows.Should().ContainSingle();
        imported.Rows[0].Symbol.Should().Be("BRK,B");
        imported.Rows[0].ActivityType.Should().Be("OTHER \"SPECIAL\"");
        imported.Rows[0].ExternalTransactionId.Should().Be("TX,\"1\"");
    }
}

public sealed class CsvStatementEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-csv-evidence-{Guid.NewGuid():N}");
    private const string Header = "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission";

    [Theory]
    [InlineData("currency-missing")]
    [InlineData("currency-blank")]
    [InlineData("currency-invalid")]
    [InlineData("quantity-comma")]
    [InlineData("price-comma")]
    [InlineData("cash-comma")]
    [InlineData("fees-comma")]
    [InlineData("fees-invalid")]
    [InlineData("fees-overflow")]
    public async Task MissingOrMalformedEvidence_RefusesValidationAndImportWithoutPersistence(string defect)
    {
        var columns = Header.Split(',').ToList();
        var values = new List<string> { "A1", "SPY", "10", "500", "-5000", "BUY", "2026-01-02", "", "USD", "1.5" };
        switch (defect)
        {
            case "currency-missing":
                columns.RemoveRange(7, 3);
                values.RemoveRange(7, 3);
                break;
            case "currency-blank":
                values[8] = " ";
                break;
            case "currency-invalid":
                values[8] = "???";
                break;
            case "quantity-comma":
                values[2] = "1,25";
                break;
            case "price-comma":
                values[3] = "1,25";
                break;
            case "cash-comma":
                values[4] = "1,25";
                break;
            case "fees-comma":
                values[9] = "1,25";
                break;
            case "fees-invalid":
                values[9] = "unknown";
                break;
            case "fees-overflow":
                values[9] = new string('9', 100);
                break;
        }
        var request = await WriteAsync(string.Join(',', columns), values);
        var store = new JsonCanonicalStatementStore(_root);
        var service = new CsvBrokerStatementService(store);
        Assert.False((await service.ValidateAsync(request)).IsValid);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(request));
        Assert.Empty(await store.ListImportsAsync());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public async Task ExplicitCurrencyAndZero_ArePreservedWithQuotedFieldsAndInvariantDecimals(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var request = await WriteAsync(Header, ["A1", "Quoted, asset", "0", "1.25", "0", "BUY", "2026-01-02", "", "eur", "0"]);
            var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(_root));
            Assert.True((await service.ValidateAsync(request)).IsValid);
            var result = await service.ImportAsync(request);
            var row = Assert.Single(result.Rows);
            Assert.Equal("EUR", row.Currency);
            Assert.Equal("Quoted, asset", row.Symbol);
            Assert.Equal(0m, row.Quantity);
            Assert.Equal(1.25m, row.Price);
            Assert.Equal(0m, row.CashAmount);
            Assert.Equal(0m, row.FeesCommission);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private async Task<BrokerStatementImportRequest> WriteAsync(string header, IEnumerable<string> values)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "statement.csv");
        var record = string.Join(',', values.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""));
        await File.WriteAllTextAsync(path, header + "\n" + record + "\n");
        return new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)) { ExternalAccountId = "A1" };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
