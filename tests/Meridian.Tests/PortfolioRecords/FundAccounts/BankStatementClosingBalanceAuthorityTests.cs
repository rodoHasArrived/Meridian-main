using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

public sealed class BankStatementClosingBalanceAuthorityTests
{
    [Fact]
    public void StatementDate_ExposesHeaderDateWithLegacyValueDateFallback()
    {
        var line = Line(90m);
        line.StatementDate.Should().Be(line.ValueDate);
        (line with { StatementAsOfDate = line.ValueDate.AddDays(3) }).StatementDate.Should().Be(line.ValueDate.AddDays(3));
    }

    [Fact]
    public void SelectAuthoritativeLines_ReplacesWholeStatementBeforeFilteringCurrencyOrSummingFlows()
    {
        var original = Line(90m) with { StatementRevision = 1, LineSequence = 1 };
        var correction = original with { BatchId = Guid.NewGuid(), Currency = "EUR", StatementRevision = 2, Amount = 20m };
        var selected = BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([correction, original]);

        selected.Should().Equal(correction);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([correction, original], "USD").Should().BeNull();
    }

    [Fact]
    public void SelectAuthoritativeLines_CompetingBatchesWithEqualClosingBalances_RemainUnavailable()
    {
        var line = Line(90m) with { StatementRevision = 1, LineSequence = 1 };
        var other = line with { BatchId = Guid.NewGuid(), Amount = 20m };
        BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectLatestLine([line, other]).Should().BeNull();
    }

    [Fact]
    public void SelectAuthoritativeLines_TransactionFeedDeltasOnOneDate_AreAdditiveNotCompeting()
    {
        var first = Line(90m) with { IsTransactionFeed = true, ClosingBalance = null };
        var second = first with { LineId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Amount = 20m };
        var selected = BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([second, first]);

        selected.Should().BeEquivalentTo([first, second]);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([second, first]).Should().BeNull("feed deltas carry no closing balance");
    }

    [Fact]
    public void SelectAuthoritativeLines_StatementOnTheSameDate_SupersedesFeedDeltas()
    {
        var delta = Line(90m) with { IsTransactionFeed = true, ClosingBalance = null };
        var statement = Line(100m) with { AccountId = delta.AccountId, StatementRevision = 1, LineSequence = 1 };
        var selected = BankStatementClosingBalanceAuthority.SelectAuthoritativeLines([delta, statement]);

        selected.Should().Equal(statement);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([delta, statement]).Should().Be(100m);
    }

    [Fact]
    public void SelectLatestLine_FeedBatchesOnOneDate_DoNotCompareTheirRestartingSequences()
    {
        // Two deliveries on one date each number their rows from 1.
        var earlierBatch = Guid.NewGuid();
        var laterBatch = Guid.NewGuid();
        var account = Guid.NewGuid();
        BankStatementLineDto Feed(Guid batch, int sequence, DateOnly valueDate) => Line(0m) with
        {
            BatchId = batch,
            AccountId = account,
            TransactionDate = new(2026, 10, 9),
            ValueDate = valueDate,
            StatementAsOfDate = new(2026, 10, 9),
            ClosingBalance = null,
            LineSequence = sequence,
            IsTransactionFeed = true
        };
        var first = Feed(earlierBatch, 1, new(2026, 10, 8));
        var second = Feed(earlierBatch, 2, new(2026, 10, 8));
        var latest = Feed(laterBatch, 1, new(2026, 10, 9));

        BankStatementClosingBalanceAuthority.SelectLatestLine([first, second, latest], "USD")
            .Should().Be(latest, "the later value date decides across deliveries, not the earlier batch's higher ordinal");
    }

    [Fact]
    public void SelectClosingBalance_LaterTransactionFeedDate_KeepsTheLatestStatementBalance()
    {
        var statement = Line(500m);
        // A Plaid delivery dated after the statement carries no running balance of its own.
        var delta = Line(0m) with
        {
            AccountId = statement.AccountId,
            TransactionDate = new(2026, 10, 9),
            ValueDate = new(2026, 10, 9),
            ClosingBalance = null,
            IsTransactionFeed = true
        };

        BankStatementClosingBalanceAuthority.SelectClosingBalance([statement, delta], "USD").Should().Be(500m);
    }

    [Fact]
    public void SelectLatestLine_UsesRevisionAndSourceOrdinalForBackdatedFinalRow()
    {
        var line = Line(90m) with { StatementAsOfDate = new(2026, 10, 7), StatementRevision = 1, LineSequence = 99 };
        var correction = line with { BatchId = Guid.NewGuid(), StatementRevision = 2, LineSequence = 1 };
        var final = correction with { LineId = Guid.NewGuid(), LineSequence = 2, ValueDate = line.ValueDate.AddDays(-1), ClosingBalance = 100m };
        BankStatementClosingBalanceAuthority.SelectLatestLine([final, line, correction]).Should().Be(final);
        BankStatementClosingBalanceAuthority.LatestStatementDate([final]).Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public async Task SourceAuthority_PersistsAcrossLocalServiceRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-statement-authority-{Guid.NewGuid():N}.json");
        try
        {
            var service = new InMemoryFundAccountService(path);
            var accountId = Guid.NewGuid();
            var date = new DateOnly(2026, 10, 7);
            await service.CreateAccountAsync(new(accountId, AccountTypeDto.Bank, $"BANK-{accountId:N}", "Bank", "USD", DateTimeOffset.UtcNow, "ops"));
            foreach (var revision in new[] { 2, 1 })
            {
                var batchId = Guid.NewGuid();
                var line = Line(revision == 2 ? 100m : 90m) with { AccountId = accountId, BatchId = batchId, LineSequence = 10 };
                await service.IngestBankStatementAsync(new(batchId, accountId, date, "Bank", null, [line], "ops", StatementRevision: revision));
            }

            var restarted = new InMemoryFundAccountService(path);
            var retained = await restarted.GetBankStatementLinesAsync(accountId);
            retained.Should().HaveCount(2);
            retained.Should().OnlyContain(line => line.LineSequence == 10 && line.StatementAsOfDate == date);
            BankStatementClosingBalanceAuthority.SelectClosingBalance(retained).Should().Be(100m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LegacyLinesWithoutSequence_AdoptSourceOrderOnExactReimport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-legacy-sequence-{Guid.NewGuid():N}.json");
        try
        {
            var service = new InMemoryFundAccountService(path);
            var accountId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            var date = new DateOnly(2026, 10, 7);
            await service.CreateAccountAsync(new(accountId, AccountTypeDto.Bank, $"BANK-{accountId:N}", "Bank", "USD", DateTimeOffset.UtcNow, "ops"));
            var earlier = Line(90m) with { AccountId = accountId, BatchId = batchId, LineSequence = 1 };
            // The final source row is backdated, so value-date fallback would pick the wrong balance.
            var final = Line(100m) with { AccountId = accountId, BatchId = batchId, LineSequence = 2, ValueDate = date.AddDays(-1) };
            var request = new IngestBankStatementRequest(batchId, accountId, date, "Bank", null, [earlier, final], "ops");
            await service.IngestBankStatementAsync(request);

            // Simulate rows retained before source order was recorded.
            var snapshot = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            StripLineSequence(snapshot);
            await File.WriteAllTextAsync(path, snapshot.ToJsonString());
            var legacy = new InMemoryFundAccountService(path);
            (await legacy.GetBankStatementLinesAsync(accountId)).Should().OnlyContain(line => line.LineSequence == null);

            await legacy.IngestBankStatementAsync(request);

            var retained = await new InMemoryFundAccountService(path).GetBankStatementLinesAsync(accountId);
            retained.Single(line => line.LineId == earlier.LineId).LineSequence.Should().Be(1);
            retained.Single(line => line.LineId == final.LineId).LineSequence.Should().Be(2);
            BankStatementClosingBalanceAuthority.SelectClosingBalance(retained).Should().Be(100m);
        }
        finally
        {
            File.Delete(path);
        }

        static void StripLineSequence(System.Text.Json.Nodes.JsonNode? node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var key in obj.Select(static pair => pair.Key).ToList())
                    {
                        if (string.Equals(key, "lineSequence", StringComparison.OrdinalIgnoreCase))
                            obj.Remove(key);
                        else
                            StripLineSequence(obj[key]);
                    }
                    break;
                case System.Text.Json.Nodes.JsonArray array:
                    foreach (var item in array)
                        StripLineSequence(item);
                    break;
            }
        }
    }

    [Fact]
    public void SelectClosingBalance_ConflictingLegacyRowsWithoutSequence_RemainsUnavailable()
    {
        var line = Line(90m);
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, line with { LineId = Guid.NewGuid(), ClosingBalance = 100m }])
            .Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_EqualRevisionConflictingBatches_RemainsUnavailableRegardlessOfOrder()
    {
        var line = Line(90m) with { LineSequence = 1, StatementRevision = 1 };
        var other = line with { BatchId = Guid.NewGuid(), ClosingBalance = 100m };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, other]).Should().BeNull();
        BankStatementClosingBalanceAuthority.SelectClosingBalance([other, line]).Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_CorrectionWithoutClosingBalance_DoesNotReuseOldRevision()
    {
        var line = Line(90m) with { LineSequence = 1, StatementRevision = 1 };
        var correction = line with { BatchId = Guid.NewGuid(), StatementRevision = 2, ClosingBalance = null };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([line, correction]).Should().BeNull();
    }

    [Fact]
    public void SelectClosingBalance_StatementDatePrecedesRevision_AndSourceSequencePrecedesValueDate()
    {
        var date = new DateOnly(2026, 10, 7);
        var oldStatement = Line(80m) with { StatementAsOfDate = date, StatementRevision = 20, LineSequence = 100 };
        var earlierValue = oldStatement with
        {
            BatchId = Guid.NewGuid(),
            StatementAsOfDate = date.AddDays(1),
            StatementRevision = 1,
            ValueDate = date,
            ClosingBalance = 90m,
            LineSequence = 1
        };
        var final = earlierValue with { LineId = Guid.NewGuid(), ValueDate = date.AddDays(-1), ClosingBalance = 100m, LineSequence = 2 };
        BankStatementClosingBalanceAuthority.SelectClosingBalance([final, oldStatement, earlierValue]).Should().Be(100m);
    }

    private static BankStatementLineDto Line(decimal balance) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 7), new(2026, 10, 7),
        10m, "USD", "Deposit", "Deposit", null, balance);
}
