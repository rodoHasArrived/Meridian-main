using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;
using Npgsql;

namespace Meridian.Tests.Storage.FundAccounts;

public sealed partial class PostgresFundAccountStoreTests
{
    [FundAccountDatabaseFact]
    public async Task Migration008_ExactHistoricalPlaidIdentity_AcceptsTheNextFeedDelivery()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        // The ASCII punctuation and mixed case distinguish ordinal-ignore-case sorting from
        // either ordinary case-sensitive sorting or sorting lowercased references.
        var (batch, lines) = HistoricalPlaidBatch(account.AccountId, ["_txn", "a_txn", "Z-txn", "0txn"]);
        await store.InsertBankStatementBatchAsync(batch, lines, cts.Token);

        await ApplyPlaidFeedBackfillAsync(cts.Token);
        await ApplyPlaidFeedBackfillAsync(cts.Token);

        var changed = lines.Select(line => line with { Amount = line.Amount - 2m }).ToArray();
        var redeliver = () => store.InsertBankStatementBatchAsync(
            batch with { IsTransactionFeed = true }, changed, cts.Token);
        await redeliver.Should().NotThrowAsync("the exact historical connector delivery is a mutable feed");
        var retained = await store.GetBankStatementLinesAsync(account.AccountId, null, null, cts.Token);
        retained.Should().HaveCount(lines.Length).And.OnlyContain(line => line.IsTransactionFeed == true);
        retained.Select(line => (line.LineId, line.Amount))
            .Should().BeEquivalentTo(changed.Select(line => (line.LineId, line.Amount)));
    }

    [FundAccountDatabaseFact]
    public async Task Migration008_BalanceFreeOperatorPlaidCsv_RemainsAnImmutableCompleteStatement()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var csv = Encoding.UTF8.GetBytes("""
            transaction_date,value_date,amount,currency,transaction_type,description,reference
            2026-10-15,2026-10-15,-25.00,USD,debit,Coffee,txn-1
            """);
        var batchId = BankStatementCsvImportMapper.BuildBatchId(csv, account.AccountId, "Plaid");
        var parsed = BankStatementCsvImportMapper.Parse(csv, account.AccountId, batchId);
        parsed.Issues.Should().NotContain(issue => issue.Severity == "Error");
        parsed.Lines.Should().ContainSingle().Which.ClosingBalance.Should().BeNull();
        var batch = new BankStatementBatchDto(batchId, account.AccountId, parsed.StatementDate!.Value,
            "Plaid", parsed.Lines.Count, DateTimeOffset.UtcNow, "operator");
        await store.InsertBankStatementBatchAsync(batch, parsed.Lines, cts.Token);

        await ApplyPlaidFeedBackfillAsync(cts.Token);

        var changed = parsed.Lines.Select(line => line with { Amount = -27m }).ToArray();
        var reclassify = () => store.InsertBankStatementBatchAsync(
            batch with { IsTransactionFeed = true }, changed, cts.Token);
        await reclassify.Should().ThrowAsync<UnauthorizedAccessException>();
        var overwrite = () => store.InsertBankStatementBatchAsync(batch, changed, cts.Token);
        await overwrite.Should().ThrowAsync<InvalidOperationException>();
        var retained = await store.GetBankStatementLinesAsync(account.AccountId, null, null, cts.Token);
        retained.Should().ContainSingle().Which.Amount.Should().Be(-25m);
        retained.Single().IsTransactionFeed.Should().NotBe(true);
    }

    [FundAccountDatabaseFact]
    public async Task Migration008_UnprovenOrIncompletePlaidLineage_RemainsACompleteStatement()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var missingReference = HistoricalPlaidBatch(account.AccountId, ["missing-reference"]);
        missingReference.Lines[0] = missingReference.Lines[0] with { Reference = null };
        var wrongLineId = HistoricalPlaidBatch(account.AccountId, ["wrong-line-id"]);
        wrongLineId.Lines[0] = wrongLineId.Lines[0] with { LineId = Guid.NewGuid() };
        var wrongBatchId = HistoricalPlaidBatch(account.AccountId, ["wrong-batch-id"]);
        wrongBatchId.Batch = wrongBatchId.Batch with { BatchId = Guid.NewGuid() };
        wrongBatchId.Lines[0] = wrongBatchId.Lines[0] with { BatchId = wrongBatchId.Batch.BatchId };
        var partial = HistoricalPlaidBatch(account.AccountId, ["partial-A", "partial-B"]);
        partial.Lines = partial.Lines.Take(1).ToArray();
        var balance = HistoricalPlaidBatch(account.AccountId, ["statement-balance"]);
        balance.Lines[0] = balance.Lines[0] with { ClosingBalance = 100m };
        var unproven = new[]
        {
            missingReference, wrongLineId, wrongBatchId, partial, balance,
            HistoricalPlaidBatch(account.AccountId, ["case-tie", "CASE-TIE"]),
            HistoricalPlaidBatch(account.AccountId, ["unsupported-é"])
        };
        foreach (var candidate in unproven)
            await store.InsertBankStatementBatchAsync(candidate.Batch, candidate.Lines, cts.Token);

        await ApplyPlaidFeedBackfillAsync(cts.Token);

        var retained = await store.GetBankStatementLinesAsync(account.AccountId, null, null, cts.Token);
        retained.Should().HaveCount(unproven.Sum(candidate => candidate.Lines.Length))
            .And.OnlyContain(line => line.IsTransactionFeed != true);
        foreach (var candidate in unproven)
        {
            var redeliver = () => store.InsertBankStatementBatchAsync(
                candidate.Batch with { IsTransactionFeed = true }, candidate.Lines, cts.Token);
            await redeliver.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }

    private async Task ApplyPlaidFeedBackfillAsync(CancellationToken ct)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "FundAccounts", "Migrations", "008_plaid_transaction_feed_backfill.sql"), ct);
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var migrate = connection.CreateCommand();
        migrate.CommandText = script.Replace("__SCHEMA__", _fixture.Options.Schema, StringComparison.Ordinal);
        await migrate.ExecuteNonQueryAsync(ct);
    }

    private static (BankStatementBatchDto Batch, BankStatementLineDto[] Lines) HistoricalPlaidBatch(
        Guid accountId, string[] references)
    {
        // Reproduce the historical connector's actual identity algorithm, including Guid byte order.
        var batchId = HistoricalPlaidGuid($"plaid-bank-batch:{accountId}:{string.Join('|', references.Order(StringComparer.OrdinalIgnoreCase))}");
        var date = new DateOnly(2026, 10, 15);
        var batch = new BankStatementBatchDto(batchId, accountId, date, "Plaid", references.Length,
            DateTimeOffset.UtcNow, "operator");
        var lines = references.Select(reference => new BankStatementLineDto(
            HistoricalPlaidGuid($"plaid-bank-line:{reference}"), batchId, accountId, date, date,
            -25m, "USD", "debit", reference, reference, ClosingBalance: null)).ToArray();
        return (batch, lines);
    }

    private static Guid HistoricalPlaidGuid(string material) => new(MD5.HashData(Encoding.UTF8.GetBytes(material)));
}
