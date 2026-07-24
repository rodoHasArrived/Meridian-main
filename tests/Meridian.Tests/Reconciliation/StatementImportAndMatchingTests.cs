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
        await File.WriteAllTextAsync(path, "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");
        var service = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var req = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 1, 31)) with
        {
            ExternalAccountId = "A1"
        };

        await service.ImportAsync(req);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(req));
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
        const string content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n";
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
        const string content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n";
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(second.ToBrokerStatementImportRequest()));
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
}
