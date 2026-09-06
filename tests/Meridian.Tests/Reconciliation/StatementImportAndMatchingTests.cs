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
        await File.WriteAllTextAsync(path, "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");
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
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
            "A1,SPY,10,500,5000,BUY,2026-01-02\n";
        const string changed =
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
            "A1,SPY,11,500,5500,BUY,2026-01-02\n";
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


public sealed class StatementStoreDurabilityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CanceledBeforeWrite_DoesNotCreateAnImportOrTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-cancel-{Guid.NewGuid():N}");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var import = new CanonicalStatementImport("canceled", "fixture", new DateOnly(2026, 6, 30),
            DateTimeOffset.UnixEpoch, "source.csv", "hash", 0, 0);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonCanonicalStatementStore(root)
                .TrySaveImportAsync(import, [], canceled.Token));
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("mid-write", false)]
    [InlineData("published", true)]
    public async Task KilledWriter_ExposesOnlyCompleteImportsAndAllowsSafeRecovery(string stage, bool published)
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ready = Path.Combine(root, "ready");
        using var child = StartWriter(root, ready, stage);
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            await WaitForReadyAsync(ready, child, errors, timeout.Token);
            var folder = Path.Combine(root, "reconciliation", "statement-imports");
            if (!published)
            {
                var partial = Assert.Single(Directory.GetFiles(folder, "*.tmp"));
                Assert.True(new FileInfo(partial).Length > 0, "The child must have written bytes before it is killed.");
                Assert.Empty(Directory.GetFiles(folder, "*.json"));
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);
            var store = new JsonCanonicalStatementStore(root);
            var retained = await store.ListImportsAsync(timeout.Token);
            Assert.Equal(published ? 1 : 0, retained.Count);
            if (published)
                AssertComplete(await store.GetImportAsync("durable-import", timeout.Token));

            using var recovery = StartWriter(root, Path.Combine(root, "recovery"), "race");
            var recoveryErrors = recovery.StandardError.ReadToEndAsync();
            try
            {
                await File.WriteAllTextAsync(Path.Combine(root, "start"), "go", timeout.Token);
                await recovery.WaitForExitAsync(timeout.Token);
                Assert.True(recovery.ExitCode == 0, await recoveryErrors);
                Assert.Equal(published ? "duplicate" : "created", await File.ReadAllTextAsync(Path.Combine(root, "recovery"), timeout.Token));
                Assert.Single(await store.ListImportsAsync(timeout.Token));
                AssertComplete(await store.GetImportAsync("durable-import", timeout.Token));
            }
            finally { await StopAsync(recovery); }
        }
        finally
        {
            await StopAsync(child);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IndependentWriters_ClaimOneCompleteImport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var children = Enumerable.Range(0, 4).Select(index => StartWriter(root, Path.Combine(root, $"result-{index}"), "race")).ToArray();
        var errors = children.Select(child => child.StandardError.ReadToEndAsync()).ToArray();
        try
        {
            for (var index = 0; index < children.Length; index++)
                await WaitForReadyAsync(Path.Combine(root, $"result-{index}.started"), children[index], errors[index], timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(root, "start"), "go", timeout.Token);
            await Task.WhenAll(children.Select(child => child.WaitForExitAsync(timeout.Token)));
            for (var index = 0; index < children.Length; index++)
                Assert.True(children[index].ExitCode == 0, await errors[index]);
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => File.ReadAllTextAsync(Path.Combine(root, $"result-{index}"), timeout.Token)));
            Assert.Equal(1, results.Count(result => result == "created"));
            Assert.Equal(3, results.Count(result => result == "duplicate"));
            var store = new JsonCanonicalStatementStore(root);
            Assert.Single(await store.ListImportsAsync(timeout.Token));
            AssertComplete(await store.GetImportAsync("durable-import", timeout.Token));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "reconciliation", "statement-imports"), "*.tmp"));
        }
        finally
        {
            foreach (var child in children)
            { await StopAsync(child); child.Dispose(); }
            Directory.Delete(root, recursive: true);
        }
    }

    private static System.Diagnostics.Process StartWriter(string root, string ready, string stage)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
            typeof(Meridian.ProcessTestHelper.ProcessTestHelperMarker).Assembly.Location, "statement-store-stage", root, ready, stage })
            start.ArgumentList.Add(argument);
        return System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Statement child did not start.");
    }

    private static async Task WaitForReadyAsync(string ready, System.Diagnostics.Process child, Task<string> errors, CancellationToken ct)
    {
        while (!File.Exists(ready))
        {
            Assert.False(child.HasExited, child.HasExited ? await errors : "");
            await Task.Delay(20, ct);
        }
    }

    private static async Task StopAsync(System.Diagnostics.Process child)
    {
        if (!child.HasExited)
            child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync();
    }

    private static void AssertComplete(BrokerStatementImportResult? result)
    {
        Assert.NotNull(result);
        Assert.Equal(256, result.Rows.Count);
        Assert.Equal(Enumerable.Range(1, 256), result.Rows.Select(row => row.SourceRowNumber));
        Assert.All(result.Rows, row => Assert.Equal(new string('A', 4096), row.RawChecksum));
    }
}
